using RecorderApp.Models;
using System.Security.Cryptography;

namespace RecorderApp.Services;

public sealed class JsonSettingsStore
{
    private const string ProtectedPasswordPrefix = "dpapi:";

    private sealed class MaintenanceState
    {
        public string LastCleanupDate { get; set; } = string.Empty;

        public string CleanupSignature { get; set; } = string.Empty;
    }

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

        settings.ExitPassword = ProtectExitPasswordInternal(settings.ExitPassword);

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
        return LoadLastCleanupState().Date;
    }

    public void SaveLastCleanupDate(DateOnly date)
    {
        SaveLastCleanupState(date, string.Empty);
    }

    public (DateOnly? Date, string Signature) LoadLastCleanupState()
    {
        if (!File.Exists(AppPaths.MaintenanceFile))
        {
            return (null, string.Empty);
        }

        var content = File.ReadAllText(AppPaths.MaintenanceFile, Encoding.UTF8).Trim();
        if (DateOnly.TryParseExact(content, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var legacyDate))
        {
            return (legacyDate, string.Empty);
        }

        try
        {
            var state = JsonSerializer.Deserialize<MaintenanceState>(content, JsonOptions);
            if (state is not null
                && DateOnly.TryParseExact(state.LastCleanupDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var stateDate))
            {
                return (stateDate, state.CleanupSignature ?? string.Empty);
            }
        }
        catch
        {
        }

        return (null, string.Empty);
    }

    public void SaveLastCleanupState(DateOnly date, string cleanupSignature)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var state = new MaintenanceState
        {
            LastCleanupDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            CleanupSignature = cleanupSignature ?? string.Empty,
        };
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(AppPaths.MaintenanceFile, json, Encoding.UTF8);
    }

    public void ClearLastCleanupDate()
    {
        if (File.Exists(AppPaths.MaintenanceFile))
        {
            File.Delete(AppPaths.MaintenanceFile);
        }
    }

    public string ProtectExitPassword(string? password)
    {
        return ProtectExitPasswordInternal(password);
    }

    public bool VerifyExitPassword(string? storedValue, string? plainText)
    {
        if (string.IsNullOrEmpty(storedValue))
        {
            return string.IsNullOrEmpty(plainText);
        }

        if (!IsProtectedExitPassword(storedValue))
        {
            return string.Equals(storedValue, plainText ?? string.Empty, StringComparison.Ordinal);
        }

        try
        {
            var cipherBytes = Convert.FromBase64String(storedValue[ProtectedPasswordPrefix.Length..]);
            var plainBytes = ProtectedData.Unprotect(cipherBytes, null, DataProtectionScope.CurrentUser);
            var decrypted = Encoding.UTF8.GetString(plainBytes);
            return string.Equals(decrypted, plainText ?? string.Empty, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
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

    private static string ProtectExitPasswordInternal(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return string.Empty;
        }

        if (IsProtectedExitPassword(password))
        {
            return password;
        }

        var bytes = Encoding.UTF8.GetBytes(password);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return ProtectedPasswordPrefix + Convert.ToBase64String(protectedBytes);
    }

    private static bool IsProtectedExitPassword(string value)
    {
        return value.StartsWith(ProtectedPasswordPrefix, StringComparison.Ordinal);
    }
}
