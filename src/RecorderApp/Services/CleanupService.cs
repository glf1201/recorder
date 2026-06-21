using RecorderApp.Models;

namespace RecorderApp.Services;

public sealed class CleanupService
{
    private const int YieldAfterDeleteCount = 5;
    private static readonly TimeSpan DeletePause = TimeSpan.FromMilliseconds(60);
    private readonly JsonSettingsStore _settingsStore;
    private readonly FileLogger _logger;
    private readonly DateTime _startupTime = DateTime.Now;

    public CleanupService(JsonSettingsStore settingsStore, FileLogger logger)
    {
        _settingsStore = settingsStore;
        _logger = logger;
    }

    public async Task RunStartupMaintenanceAsync(RecorderSettings settings, string? activeRecordingPath, CancellationToken cancellationToken)
    {
        if (ShouldCompensateScheduledCleanup(settings, DateTime.Now))
        {
            await DeleteExpiredFilesAsync(settings, activeRecordingPath, cancellationToken);
            _settingsStore.SaveLastCleanupDate(DateOnly.FromDateTime(DateTime.Now));
        }

        EnforceDiskProtection(settings.StoragePath, activeRecordingPath);
    }

    public async Task<string> RunMaintenanceAsync(RecorderSettings settings, string? activeRecordingPath, CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        var lastCleanupDate = _settingsStore.LoadLastCleanupDate();
        if (TryParseCleanupTime(settings.CleanupTime, out var cleanupTime)
            && now.TimeOfDay >= cleanupTime
            && lastCleanupDate != DateOnly.FromDateTime(now))
        {
            await DeleteExpiredFilesAsync(settings, activeRecordingPath, cancellationToken);
            _settingsStore.SaveLastCleanupDate(DateOnly.FromDateTime(now));
        }

        var diskStatus = EnforceDiskProtection(settings.StoragePath, activeRecordingPath);
        return diskStatus;
    }

    private bool ShouldCompensateScheduledCleanup(RecorderSettings settings, DateTime now)
    {
        if (!TryParseCleanupTime(settings.CleanupTime, out var cleanupTime))
        {
            return false;
        }

        var lastCleanupDate = _settingsStore.LoadLastCleanupDate();
        return now - _startupTime <= TimeSpan.FromMinutes(10)
            && now.TimeOfDay >= cleanupTime
            && lastCleanupDate != DateOnly.FromDateTime(now);
    }

    private async Task DeleteExpiredFilesAsync(RecorderSettings settings, string? activeRecordingPath, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.Now.Date.AddDays(-Math.Clamp(settings.RetentionDays, 3, 30));
        await DeleteExpiredRecordingFoldersAsync(
            settings.StoragePath,
            activeRecordingPath,
            cutoff,
            cancellationToken);

        var recordingPath = NormalizePath(settings.StoragePath);
        foreach (var directory in settings.GetAdditionalCleanupDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.Equals(NormalizePath(directory), recordingPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await DeleteExpiredAdditionalFoldersAsync(directory, cutoff, cancellationToken);
        }
    }

    private async Task DeleteExpiredRecordingFoldersAsync(
        string root,
        string? activeRecordingPath,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        var expiredDirectories = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .Select(path => new DirectoryInfo(path))
            .Where(directory => IsExpiredRecordingDirectory(directory, cutoff))
            .OrderBy(directory => directory.Name)
            .ToList();

        var processedCount = 0;
        foreach (var directory in expiredDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ContainsActiveRecording(directory.FullName, activeRecordingPath))
            {
                continue;
            }

            if (TryDeleteDirectory(directory, "Deleted expired recording directory"))
            {
                processedCount++;
                await PauseDeletionLoopIfNeededAsync(processedCount, cancellationToken);
            }
        }
    }

    private async Task DeleteExpiredAdditionalFoldersAsync(
        string? root,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        var expiredDirectories = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .Select(path => new DirectoryInfo(path))
            .Where(directory => IsExpiredAdditionalDirectory(directory, cutoff))
            .OrderBy(directory => directory.Name)
            .ToList();

        var processedCount = 0;
        foreach (var directory in expiredDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryDeleteDirectory(directory, "Deleted expired cleanup directory"))
            {
                processedCount++;
                await PauseDeletionLoopIfNeededAsync(processedCount, cancellationToken);
            }
        }
    }

    private string EnforceDiskProtection(string root, string? activeRecordingPath)
    {
        Directory.CreateDirectory(root);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
        var freeGb = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
        if (freeGb < 5)
        {
            _logger.Warn($"Disk free space warning: {freeGb:F2} GB left on {drive.Name}");
        }

        if (freeGb < 1)
        {
            var candidates = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(file => HasVideoExtension(file) && !PathsEqual(file, activeRecordingPath))
                .Select(file => new FileInfo(file))
                .OrderBy(info => info.CreationTimeUtc)
                .ToList();

            foreach (var candidate in candidates)
            {
                try
                {
                    candidate.Delete();
                    _logger.Warn($"Deleted old file for disk protection: {candidate.FullName}");
                    drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
                    freeGb = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
                    if (freeGb >= 5)
                    {
                        break;
                    }
                }
                catch (Exception exception)
                {
                    _logger.Warn($"Failed to delete file {candidate.FullName}. {exception.Message}");
                }
            }
        }

        return $"{drive.Name} free {freeGb:F2} GB";
    }

    private static bool TryParseCleanupTime(string value, out TimeSpan time)
    {
        return TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out time);
    }

    private static bool HasVideoExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".mkv", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task PauseDeletionLoopIfNeededAsync(int processedCount, CancellationToken cancellationToken)
    {
        if (processedCount % YieldAfterDeleteCount == 0)
        {
            await Task.Delay(DeletePause, cancellationToken);
        }
    }

    private static bool PathsEqual(string left, string? right)
    {
        return !string.IsNullOrWhiteSpace(right)
            && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private bool TryDeleteDirectory(DirectoryInfo directory, string message)
    {
        try
        {
            directory.Delete(true);
            _logger.Info($"{message}: {directory.FullName}");
            return true;
        }
        catch (Exception exception)
        {
            _logger.Warn($"Failed to delete expired directory {directory.FullName}. {exception.Message}");
            return false;
        }
    }

    private static bool IsExpiredRecordingDirectory(DirectoryInfo directory, DateTime cutoff)
    {
        return DateTime.TryParseExact(
                directory.Name,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var folderDate)
            && folderDate.Date < cutoff.Date;
    }

    private static bool IsExpiredAdditionalDirectory(DirectoryInfo directory, DateTime cutoff)
    {
        return directory.CreationTime.Date < cutoff.Date;
    }

    private static bool ContainsActiveRecording(string directoryPath, string? activeRecordingPath)
    {
        if (string.IsNullOrWhiteSpace(activeRecordingPath))
        {
            return false;
        }

        try
        {
            var normalizedDirectory = AppendDirectorySeparator(Path.GetFullPath(directoryPath));
            var normalizedActivePath = Path.GetFullPath(activeRecordingPath);
            return normalizedActivePath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string AppendDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
