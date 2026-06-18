using RecorderApp.Models;

namespace RecorderApp.Services;

public sealed class RecordingRecoveryService
{
    private readonly JsonSettingsStore _settingsStore;
    private readonly FileLogger _logger;

    public RecordingRecoveryService(JsonSettingsStore settingsStore, FileLogger logger)
    {
        _settingsStore = settingsStore;
        _logger = logger;
    }

    public async Task RecoverIfNeededAsync(string ffmpegPath, CancellationToken cancellationToken)
    {
        var state = _settingsStore.LoadSessionState();
        if (state is null || state.CleanShutdown || string.IsNullOrWhiteSpace(state.CurrentOutputPath))
        {
            return;
        }

        var filePath = state.CurrentOutputPath;
        _logger.Warn($"Detected an unclean shutdown. Trying to repair: {filePath}");
        if (!File.Exists(filePath))
        {
            _settingsStore.ClearSessionState();
            return;
        }

        if (IsFileLocked(filePath))
        {
            _logger.Warn($"Recovery skipped because the recording file is still in use: {filePath}");
            return;
        }

        var repairedPath = Path.Combine(
            Path.GetDirectoryName(filePath) ?? AppPaths.DefaultRecordDirectory,
            Path.GetFileNameWithoutExtension(filePath) + "_repaired" + Path.GetExtension(filePath));
        var audioPath = state.CurrentAudioPath;

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-hide_banner -y -err_detect ignore_err -i {Quote(filePath)} -c copy {Quote(repairedPath)}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return;
        }

        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode == 0 && File.Exists(repairedPath))
        {
            try
            {
                File.Delete(filePath);
                File.Move(repairedPath, filePath, true);
            }
            catch (IOException exception)
            {
                _logger.Warn($"Recovery postponed because the file is in use. {exception.Message}");
                return;
            }

            await TryMuxRecoveredAudioAsync(ffmpegPath, filePath, audioPath, cancellationToken);
            _logger.Info($"Repair finished: {filePath}");
        }
        else
        {
            _logger.Warn($"Repair failed for {filePath}. ffmpeg output: {error}");
            if (File.Exists(repairedPath))
            {
                TryDeleteQuietly(repairedPath);
            }
        }

        _settingsStore.ClearSessionState();
    }

    private async Task TryMuxRecoveredAudioAsync(string ffmpegPath, string videoPath, string? audioPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
        {
            return;
        }

        var audioInfo = new FileInfo(audioPath);
        if (audioInfo.Length <= 44)
        {
            TryDeleteQuietly(audioPath);
            return;
        }

        if (IsFileLocked(videoPath) || IsFileLocked(audioPath))
        {
            _logger.Warn($"Recovered audio mux skipped because source files are still in use: {videoPath}");
            return;
        }

        var muxedPath = Path.Combine(
            Path.GetDirectoryName(videoPath) ?? AppPaths.DefaultRecordDirectory,
            Path.GetFileNameWithoutExtension(videoPath) + "_recovered_mux" + Path.GetExtension(videoPath));

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-hide_banner -y -i {Quote(videoPath)} -i {Quote(audioPath)} -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 128k -af apad -shortest {Quote(muxedPath)}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return;
        }

        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode == 0 && File.Exists(muxedPath))
        {
            try
            {
                File.Delete(videoPath);
                File.Move(muxedPath, videoPath, true);
                File.Delete(audioPath);
            }
            catch (IOException exception)
            {
                _logger.Warn($"Recovered audio mux postponed because the file is in use. {exception.Message}");
                return;
            }

            _logger.Info($"Recovered system audio was muxed into {videoPath}");
            return;
        }

        _logger.Warn($"Recovered audio mux failed for {videoPath}. ffmpeg output: {error}");
        if (File.Exists(muxedPath))
        {
            TryDeleteQuietly(muxedPath);
        }
    }

    private static bool IsFileLocked(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private void TryDeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
        {
            _logger.Warn($"Failed to delete temporary recovery file '{path}'. {exception.Message}");
        }
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"") + '"';
}
