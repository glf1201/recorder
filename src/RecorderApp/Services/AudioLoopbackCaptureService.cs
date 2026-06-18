using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.IO.Pipes;
using System.Diagnostics;
using System.Threading.Channels;

namespace RecorderApp.Services;

public sealed class AudioLoopbackCaptureService
{
    private readonly FileLogger _logger;

    public AudioLoopbackCaptureService(FileLogger logger)
    {
        _logger = logger;
    }

    public AudioLoopbackCaptureSession? TryCreateSession(string deviceName)
    {
        try
        {
            var device = ResolveDevice(deviceName);
            if (device is null)
            {
                _logger.Warn($"Audio loopback capture skipped because device '{deviceName}' was not found.");
                return null;
            }

            return AudioLoopbackCaptureSession.Create(device, _logger);
        }
        catch (Exception exception)
        {
            _logger.Warn($"Failed to prepare system-audio capture. {exception.Message}");
            return null;
        }
    }

    private static MMDevice? ResolveDevice(string deviceName)
    {
        using var enumerator = new MMDeviceEnumerator();

        if (string.IsNullOrWhiteSpace(deviceName) || string.Equals(deviceName, "default", StringComparison.OrdinalIgnoreCase))
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            if (string.Equals(device.FriendlyName, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }

            device.Dispose();
        }

        return null;
    }
}

public sealed class AudioLoopbackCaptureSession : IDisposable
{
    private readonly MMDevice _device;
    private readonly WasapiLoopbackCapture _capture;
    private readonly FileLogger _logger;
    private readonly NamedPipeServerStream _pipeServer;
    private readonly Channel<byte[]> _bufferChannel;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly TaskCompletionSource _connectionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _captureStoppedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _pipeName;
    private readonly Stopwatch _captureClock = new();
    private Task? _writerTask;
    private long _bytesQueued;
    private int _disposed;
    private int _pipeClosed;
    private int _startRequested;
    private int _stopRequested;

    private AudioLoopbackCaptureSession(MMDevice device, WasapiLoopbackCapture capture, NamedPipeServerStream pipeServer, FileLogger logger, string pipeName)
    {
        _device = device;
        _capture = capture;
        _pipeServer = pipeServer;
        _logger = logger;
        _pipeName = pipeName;
        _bufferChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2048)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    }

    public string PipePath => @"\\.\pipe\" + _pipeName;

    public int SampleRate => _capture.WaveFormat.SampleRate;

    public int Channels => _capture.WaveFormat.Channels;

    public string FfmpegSampleFormat => ResolveFfmpegSampleFormat(_capture.WaveFormat);

    public static AudioLoopbackCaptureSession Create(MMDevice device, FileLogger logger)
    {
        var capture = new WasapiLoopbackCapture(device);
        var pipeName = "RecorderApp-Audio-" + Guid.NewGuid().ToString("N");
        var pipeServer = new NamedPipeServerStream(
            pipeName,
            PipeDirection.Out,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);

        var session = new AudioLoopbackCaptureSession(device, capture, pipeServer, logger, pipeName);
        session.AttachHandlers();
        return session;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _startRequested, 1) == 1)
        {
            return;
        }

        _writerTask = Task.Run(() => RunWriterLoopAsync(_lifetimeCts.Token));
        _captureClock.Start();
        _capture.StartRecording();
        _logger.Info($"System-audio capture started: {_device.FriendlyName}");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
        await _connectionSource.Task.WaitAsync(timeoutCts.Token);
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) == 0)
        {
            EnqueueTrailingSilence();
            try
            {
                _capture.StopRecording();
            }
            catch (Exception exception)
            {
                _logger.Warn($"Failed to stop system-audio capture cleanly. {exception.Message}");
                _captureStoppedSource.TrySetResult();
            }

            _bufferChannel.Writer.TryComplete();
        }

        try
        {
            await _captureStoppedSource.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException)
        {
        }

        if (_writerTask is not null)
        {
            try
            {
                await _writerTask.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (TimeoutException)
            {
            }
            catch (OperationCanceledException)
            {
            }
        }

        ClosePipeServer();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _lifetimeCts.Cancel();
        _bufferChannel.Writer.TryComplete();
        ClosePipeServer();

        try
        {
            _capture.Dispose();
        }
        catch
        {
        }

        try
        {
            _device.Dispose();
        }
        catch
        {
        }

        _lifetimeCts.Dispose();
    }

    private void AttachHandlers()
    {
        _capture.DataAvailable += (_, args) =>
        {
            if (args.BytesRecorded <= 0)
            {
                return;
            }

            EnqueueSilenceBeforeBuffer(args.BytesRecorded);
            var buffer = new byte[args.BytesRecorded];
            Buffer.BlockCopy(args.Buffer, 0, buffer, 0, args.BytesRecorded);
            if (_bufferChannel.Writer.TryWrite(buffer))
            {
                Interlocked.Add(ref _bytesQueued, buffer.Length);
            }
        };

        _capture.RecordingStopped += (_, args) =>
        {
            if (args.Exception is not null && Interlocked.CompareExchange(ref _stopRequested, 0, 0) == 0)
            {
                _logger.Warn($"System-audio capture stopped with an error. {args.Exception.Message}");
            }
            else
            {
                _logger.Info("System-audio capture stopped.");
            }

            _bufferChannel.Writer.TryComplete(args.Exception);
            _captureStoppedSource.TrySetResult();
        };
    }

    private async Task RunWriterLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _pipeServer.WaitForConnectionAsync(cancellationToken);
            _connectionSource.TrySetResult();

            await foreach (var buffer in _bufferChannel.Reader.ReadAllAsync(cancellationToken))
            {
                await _pipeServer.WriteAsync(buffer, cancellationToken);
                await _pipeServer.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _connectionSource.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            if (Interlocked.CompareExchange(ref _stopRequested, 0, 0) == 0)
            {
                _logger.Warn($"System-audio pipe writer stopped unexpectedly. {exception.Message}");
            }

            _connectionSource.TrySetException(exception);
        }
    }

    private void ClosePipeServer()
    {
        if (Interlocked.Exchange(ref _pipeClosed, 1) == 1)
        {
            return;
        }

        try
        {
            _pipeServer.Dispose();
        }
        catch
        {
        }
    }

    private void EnqueueSilenceBeforeBuffer(int currentBufferBytes)
    {
        var expectedBytes = AlignToBlock((long)(_captureClock.Elapsed.TotalSeconds * _capture.WaveFormat.AverageBytesPerSecond));
        var bytesBeforeCurrentBuffer = Math.Max(0, expectedBytes - currentBufferBytes);
        var queuedBytes = Interlocked.Read(ref _bytesQueued);
        var missingBytes = bytesBeforeCurrentBuffer - queuedBytes;
        if (missingBytes > 0)
        {
            EnqueueSilenceBytes(missingBytes);
        }
    }

    private void EnqueueTrailingSilence()
    {
        var expectedBytes = AlignToBlock((long)(_captureClock.Elapsed.TotalSeconds * _capture.WaveFormat.AverageBytesPerSecond));
        var queuedBytes = Interlocked.Read(ref _bytesQueued);
        var missingBytes = expectedBytes - queuedBytes;
        if (missingBytes > 0)
        {
            EnqueueSilenceBytes(missingBytes);
        }
    }

    private void EnqueueSilenceBytes(long bytes)
    {
        var remaining = AlignToBlock(bytes);
        while (remaining > 0)
        {
            var chunkSize = (int)Math.Min(remaining, 8192);
            var alignedChunkSize = (int)AlignToBlock(chunkSize);
            if (alignedChunkSize <= 0)
            {
                return;
            }

            var silence = new byte[alignedChunkSize];
            if (_bufferChannel.Writer.TryWrite(silence))
            {
                Interlocked.Add(ref _bytesQueued, alignedChunkSize);
            }

            remaining -= alignedChunkSize;
        }
    }

    private long AlignToBlock(long bytes)
    {
        var blockAlign = _capture.WaveFormat.BlockAlign;
        return bytes - (bytes % blockAlign);
    }

    private static string ResolveFfmpegSampleFormat(WaveFormat waveFormat)
    {
        if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat && waveFormat.BitsPerSample == 32)
        {
            return "f32le";
        }

        if (waveFormat.Encoding == WaveFormatEncoding.Pcm && waveFormat.BitsPerSample == 16)
        {
            return "s16le";
        }

        if (waveFormat.Encoding == WaveFormatEncoding.Pcm && waveFormat.BitsPerSample == 24)
        {
            return "s24le";
        }

        if (waveFormat.Encoding == WaveFormatEncoding.Pcm && waveFormat.BitsPerSample == 32)
        {
            return "s32le";
        }

        throw new NotSupportedException($"Unsupported loopback wave format: {waveFormat.Encoding}, {waveFormat.BitsPerSample} bit.");
    }
}
