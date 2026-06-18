using RecorderApp.Models;

namespace RecorderApp.Services;

public sealed class CleanupService
{
    private const int MaxScheduledDeletesPerRun = 30;
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
        var remainingDeletes = MaxScheduledDeletesPerRun;
        remainingDeletes = await DeleteExpiredFilesInRecordingDirectoryAsync(
            settings.StoragePath,
            activeRecordingPath,
            cutoff,
            remainingDeletes,
            cancellationToken);

        var recordingPath = NormalizePath(settings.StoragePath);
        foreach (var directory in settings.GetAdditionalCleanupDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (remainingDeletes <= 0)
            {
                _logger.Info("Scheduled cleanup reached the current batch limit. Remaining files will be deleted in later runs.");
                break;
            }

            if (string.Equals(NormalizePath(directory), recordingPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            remainingDeletes = await DeleteExpiredFilesInAdditionalDirectoryAsync(directory, cutoff, remainingDeletes, cancellationToken);
        }
    }

    private async Task<int> DeleteExpiredFilesInRecordingDirectoryAsync(
        string root,
        string? activeRecordingPath,
        DateTime cutoff,
        int remainingDeletes,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root) || remainingDeletes <= 0)
        {
            return remainingDeletes;
        }

        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(file => HasVideoExtension(file) && !PathsEqual(file, activeRecordingPath))
            .Select(file => new FileInfo(file))
            .Where(info => info.CreationTime < cutoff)
            .OrderBy(info => info.CreationTime)
            .Take(remainingDeletes)
            .ToList();

        var processedCount = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryDeleteFile(file, "Deleted expired recording file"))
            {
                remainingDeletes--;
                processedCount++;
                await PauseDeletionLoopIfNeededAsync(processedCount, cancellationToken);
            }
        }

        return remainingDeletes;
    }

    private async Task<int> DeleteExpiredFilesInAdditionalDirectoryAsync(
        string? root,
        DateTime cutoff,
        int remainingDeletes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) || remainingDeletes <= 0)
        {
            return remainingDeletes;
        }

        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Select(file => new FileInfo(file))
            .Where(info => info.CreationTime < cutoff)
            .OrderBy(info => info.CreationTime)
            .Take(remainingDeletes)
            .ToList();

        var processedCount = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryDeleteFile(file, "Deleted expired cleanup file"))
            {
                remainingDeletes--;
                processedCount++;
                await PauseDeletionLoopIfNeededAsync(processedCount, cancellationToken);
            }
        }

        DeleteEmptyDirectories(root);
        return remainingDeletes;
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

    private bool TryDeleteFile(FileInfo file, string message)
    {
        try
        {
            file.Delete();
            _logger.Info($"{message}: {file.FullName}");
            return true;
        }
        catch (Exception exception)
        {
            _logger.Warn($"Failed to delete expired file {file.FullName}. {exception.Message}");
            return false;
        }
    }

    private static async Task PauseDeletionLoopIfNeededAsync(int processedCount, CancellationToken cancellationToken)
    {
        if (processedCount % YieldAfterDeleteCount == 0)
        {
            await Task.Delay(DeletePause, cancellationToken);
        }
    }

    private void DeleteEmptyDirectories(string root)
    {
        var directories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .OrderByDescending(path => path.Length)
            .ToList();

        foreach (var directory in directories)
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, false);
                }
            }
            catch
            {
            }
        }
    }

    private static bool PathsEqual(string left, string? right)
    {
        return !string.IsNullOrWhiteSpace(right)
            && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
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
}
