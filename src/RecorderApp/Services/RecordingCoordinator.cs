using RecorderApp.Models;

namespace RecorderApp.Services;

public sealed class RecordingCoordinator
{
    private readonly JsonSettingsStore _settingsStore;
    private readonly FileLogger _logger;
    private readonly FFmpegLocator _ffmpegLocator;
    private readonly FFmpegCapabilityService _capabilityService;
    private readonly FFmpegCommandBuilder _commandBuilder;
    private readonly AudioLoopbackCaptureService _audioCaptureService;
    private readonly SegmentPlanner _segmentPlanner;
    private readonly RecordingRecoveryService _recoveryService;
    private readonly CleanupService _cleanupService;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _workerTask;
    private Process? _activeProcess;
    private AudioLoopbackCaptureSession? _activeAudioSession;
    private volatile bool _stopRequested;

    public RecordingCoordinator(
        JsonSettingsStore settingsStore,
        FileLogger logger,
        FFmpegLocator ffmpegLocator,
        FFmpegCapabilityService capabilityService,
        FFmpegCommandBuilder commandBuilder,
        AudioLoopbackCaptureService audioCaptureService,
        SegmentPlanner segmentPlanner,
        RecordingRecoveryService recoveryService,
        CleanupService cleanupService)
    {
        _settingsStore = settingsStore;
        _logger = logger;
        _ffmpegLocator = ffmpegLocator;
        _capabilityService = capabilityService;
        _commandBuilder = commandBuilder;
        _audioCaptureService = audioCaptureService;
        _segmentPlanner = segmentPlanner;
        _recoveryService = recoveryService;
        _cleanupService = cleanupService;
    }

    public event EventHandler<RecordingStatusSnapshot>? StatusChanged;

    public bool IsRunning => _workerTask is { IsCompleted: false };

    public Task StartAsync(RecorderSettings settings)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        var snapshot = settings.Clone();
        AppPaths.EnsureDirectories(snapshot.StoragePath);
        _stopRequested = false;
        _cancellationTokenSource = new CancellationTokenSource();
        _workerTask = Task.Run(() => RunAsync(snapshot, _cancellationTokenSource.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(string reason = "Manual stop")
    {
        if (_cancellationTokenSource is null)
        {
            return;
        }

        _stopRequested = true;
        _logger.Info($"Recording stop requested. Reason: {reason}");
        PublishStatus("Recording", "正在保存当前片段...", string.Empty, string.Empty, string.Empty);
        var activeAudioSession = _activeAudioSession;
        if (activeAudioSession is not null)
        {
            await activeAudioSession.StopAsync();
        }
        RequestActiveProcessStop();

        if (_workerTask is not null)
        {
            try
            {
                var completedTask = await Task.WhenAny(_workerTask, Task.Delay(TimeSpan.FromSeconds(20)));
                if (completedTask != _workerTask)
                {
                    _logger.Warn("Graceful stop timed out. Force-stopping ffmpeg.");
                    _cancellationTokenSource.Cancel();
                    TryKillActiveProcess();
                }

                await _workerTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.Error("Recording worker stopped with an unexpected error.", exception);
            }
        }

        _settingsStore.SaveSessionState(new RecordingSessionState
        {
            CleanShutdown = true,
            LastHeartbeatUtc = DateTime.UtcNow,
            LastUpdatedUtc = DateTime.UtcNow,
        });
        _settingsStore.ClearSessionState();
        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = null;
        _workerTask = null;
        PublishStatus("Stopped", "Recording stopped.", string.Empty, string.Empty, string.Empty);
    }

    private async Task RunAsync(RecorderSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !_stopRequested)
            {
                var ffmpegPath = _ffmpegLocator.Locate();
                if (string.IsNullOrWhiteSpace(ffmpegPath))
                {
                    PublishStatus("Error", "ffmpeg.exe not found. Put it under Tools/ffmpeg/bin or PATH.", string.Empty, string.Empty, string.Empty);
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    continue;
                }

                await _recoveryService.RecoverIfNeededAsync(ffmpegPath, cancellationToken);
                await _cleanupService.RunStartupMaintenanceAsync(settings, null, cancellationToken);

                (DateTime Start, DateTime End)? activeWindow = null;
                while (!cancellationToken.IsCancellationRequested && !_stopRequested)
                {
                    activeWindow = _segmentPlanner.GetCurrentWindow(DateTime.Now, activeWindow);
                    try
                    {
                        var diskStatus = await RecordSegmentAsync(ffmpegPath, settings, activeWindow.Value, cancellationToken);
                        if (DateTime.Now >= activeWindow.Value.End.AddSeconds(-1))
                        {
                            activeWindow = null;
                        }

                        PublishStatus("Waiting", "Preparing next segment.", string.Empty, string.Empty, diskStatus);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        _logger.Error("Segment recording failed.", exception);
                        PublishStatus("Recovering", exception.Message, string.Empty, string.Empty, string.Empty);
                        activeWindow = null;
                        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Recording loop cancelled.");
        }
        catch (Exception exception)
        {
            _logger.Error("Recording loop crashed.", exception);
            PublishStatus("Error", exception.Message, string.Empty, string.Empty, string.Empty);
        }
    }

    private async Task<string> RecordSegmentAsync(string ffmpegPath, RecorderSettings settings, (DateTime Start, DateTime End) window, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(settings.StoragePath, window.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);

        var extension = _commandBuilder.GetExtension(settings.Container);
        var outputPath = Path.Combine(directory, $"{window.Start:yyyy-MM-dd_HH-mm}.{extension}");
        var attempt = 0;
        var encoder = await _capabilityService.SelectEncoderAsync(ffmpegPath, settings.PreferredCodec, cancellationToken);

        while (DateTime.Now < window.End && !cancellationToken.IsCancellationRequested && !_stopRequested)
        {
            attempt++;
            var remainingSeconds = Math.Max(2, (int)Math.Ceiling((window.End - DateTime.Now).TotalSeconds));
            if (File.Exists(outputPath))
            {
                var partialPath = Path.Combine(directory, $"{window.Start:yyyy-MM-dd_HH-mm}_partial_{attempt}{Path.GetExtension(outputPath)}");
                File.Move(outputPath, partialPath, true);
                _logger.Warn($"Existing partial file moved to {partialPath}");
            }

            using var audioSession = StartSystemAudioCapture(settings);
            var includeAudio = audioSession is not null;
            var arguments = _commandBuilder.BuildArguments(settings, outputPath, encoder, remainingSeconds, audioSession);
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new InvalidOperationException("Unable to start ffmpeg process.");
            }

            _activeProcess = process;
            _activeAudioSession = audioSession;
            process.OutputDataReceived += (_, args) => { if (!string.IsNullOrWhiteSpace(args.Data)) _logger.Info("ffmpeg: " + args.Data); };
            process.ErrorDataReceived += (_, args) => { if (!string.IsNullOrWhiteSpace(args.Data)) _logger.Info("ffmpeg: " + args.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (audioSession is not null)
            {
                await audioSession.StartAsync(cancellationToken);
            }

            var recordingMessage = includeAudio
                ? $"Segment {window.Start:HH:mm} - {window.End:HH:mm}（系统声音录制中）"
                : $"Segment {window.Start:HH:mm} - {window.End:HH:mm}（当前为静音录屏）";
            PublishStatus("Recording", recordingMessage, outputPath, encoder, string.Empty);
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var heartbeatTask = Task.Run(() => WriteHeartbeatLoopAsync(outputPath, null, heartbeatCts.Token));

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKillActiveProcess();
                throw;
            }
            finally
            {
                heartbeatCts.Cancel();
                await IgnoreCancellationAsync(heartbeatTask);
                if (audioSession is not null)
                {
                    await audioSession.StopAsync();
                }
                _activeProcess = null;
                _activeAudioSession = null;
            }

            if (process.ExitCode == 0)
            {
                _logger.Info($"Segment finished successfully: {outputPath}");
                break;
            }

            _logger.Warn($"ffmpeg exited with code {process.ExitCode}. Retrying within the same segment.");
            PublishStatus("Recovering", $"ffmpeg exited with {process.ExitCode}, retrying...", outputPath, encoder, string.Empty);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }

        return await _cleanupService.RunMaintenanceAsync(settings, outputPath, cancellationToken);
    }

    private async Task WriteHeartbeatLoopAsync(string outputPath, string? audioPath, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                _settingsStore.SaveSessionState(new RecordingSessionState
                {
                    CleanShutdown = false,
                    CurrentOutputPath = outputPath,
                    CurrentAudioPath = audioPath,
                    LastHeartbeatUtc = DateTime.UtcNow,
                    LastUpdatedUtc = DateTime.UtcNow,
                });

                File.WriteAllText(AppPaths.HeartbeatFile, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), Encoding.UTF8);
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private AudioLoopbackCaptureSession? StartSystemAudioCapture(RecorderSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AudioDevice))
        {
            return null;
        }

        var session = _audioCaptureService.TryCreateSession(settings.AudioDevice);
        if (session is null)
        {
            _logger.Warn("System-audio capture is unavailable on this machine. Falling back to video-only recording.");
        }

        return session;
    }

    private void PublishStatus(string state, string message, string currentFile, string encoder, string diskStatus)
    {
        StatusChanged?.Invoke(this, new RecordingStatusSnapshot
        {
            State = state,
            Message = message,
            CurrentFile = currentFile,
            Encoder = encoder,
            DiskStatus = diskStatus,
        });
    }

    private void TryKillActiveProcess()
    {
        try
        {
            if (_activeProcess is { HasExited: false })
            {
                _activeProcess.Kill(true);
            }
        }
        catch (Exception exception)
        {
            _logger.Warn($"Failed to kill ffmpeg process. {exception.Message}");
        }
    }

    private void RequestActiveProcessStop()
    {
        try
        {
            if (_activeProcess is { HasExited: false } && _activeProcess.StartInfo.RedirectStandardInput)
            {
                _activeProcess.StandardInput.Write("q");
                _activeProcess.StandardInput.Flush();
            }
        }
        catch (Exception exception)
        {
            _logger.Warn($"Failed to request graceful ffmpeg stop. {exception.Message}");
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"") + '"';
}
