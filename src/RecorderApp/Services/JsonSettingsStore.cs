using RecorderApp.Models;

namespace RecorderApp.Services;

public sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public RecorderSettings Load()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);

        if (!File.Exists(AppPaths.SettingsFile))
        {
            var defaults = CreateDefaultSettings();
            Save(defaults);
            return defaults;
        }

        var json = File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8);
        var settings = JsonSerializer.Deserialize<RecorderSettings>(json, JsonOptions) ?? CreateDefaultSettings();
        if (string.IsNullOrWhiteSpace(settings.StoragePath))
        {
            settings.StoragePath = AppPaths.DefaultRecordDirectory;
        }

        return settings;
    }

    public void Save(RecorderSettings settings)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(AppPaths.SettingsFile, json, Encoding.UTF8);
    }

    public RecordingSessionState? LoadSessionState()
    {
        if (!File.Exists(AppPaths.SessionFile))
        {
            return null;
        }

        var json = File.ReadAllText(AppPaths.SessionFile, Encoding.UTF8);
        return JsonSerializer.Deserialize<RecordingSessionState>(json, JsonOptions);
    }

    public void SaveSessionState(RecordingSessionState state)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(AppPaths.SessionFile, json, Encoding.UTF8);
    }

    public void ClearSessionState()
    {
        if (File.Exists(AppPaths.SessionFile))
        {
            File.Delete(AppPaths.SessionFile);
        }
    }

    public DateOnly? LoadLastCleanupDate()
    {
        if (!File.Exists(AppPaths.MaintenanceFile))
        {
            return null;
        }

        var content = File.ReadAllText(AppPaths.MaintenanceFile, Encoding.UTF8).Trim();
        if (DateOnly.TryParseExact(content, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            return value;
        }

        return null;
    }

    public void SaveLastCleanupDate(DateOnly date)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        File.WriteAllText(AppPaths.MaintenanceFile, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Encoding.UTF8);
    }

    private static RecorderSettings CreateDefaultSettings()
    {
        return new RecorderSettings
        {
            StoragePath = AppPaths.DefaultRecordDirectory,
            CleanupDirectory1 = string.Empty,
            CleanupDirectory2 = string.Empty,
            CleanupDirectory3 = string.Empty,
            CleanupDirectory4 = string.Empty,
            CleanupDirectory5 = string.Empty,
            Quality = 28,
            FrameRate = 5,
            PreferredCodec = "H264",
            Container = "mkv",
            AudioDevice = "default",
            RetentionDays = 7,
            CleanupTime = "15:00",
            AutoStartWithWindows = true,
            AutoStartRecording = true,
            ExitPassword = string.Empty,
            DisplayTarget = "AllDisplays",
        };
    }
}
