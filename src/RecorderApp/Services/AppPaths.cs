namespace RecorderApp.Services;

public static class AppPaths
{
    public static string BaseDirectory => AppContext.BaseDirectory;

    public static string DataDirectory => Path.Combine(BaseDirectory, "Data");

    public static string LogDirectory => Path.Combine(BaseDirectory, "Logs");

    public static string ToolsDirectory => Path.Combine(BaseDirectory, "Tools");

    public static string DefaultRecordDirectory => Path.Combine(BaseDirectory, "Record");

    public static string DownloadCacheDirectory => Path.Combine(DataDirectory, "Downloads");

    public static string FfmpegDirectory => Path.Combine(ToolsDirectory, "ffmpeg");

    public static string FfmpegBinDirectory => Path.Combine(FfmpegDirectory, "bin");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string SessionFile => Path.Combine(DataDirectory, "session-state.json");

    public static string MaintenanceFile => Path.Combine(DataDirectory, "maintenance-state.json");

    public static string FfmpegBundledPath => Path.Combine(FfmpegBinDirectory, "ffmpeg.exe");

    public static string HeartbeatFile => Path.Combine(DataDirectory, "heartbeat.txt");

    public static void EnsureDirectories(string recordDirectory)
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(recordDirectory);
        Directory.CreateDirectory(ToolsDirectory);
        Directory.CreateDirectory(FfmpegBinDirectory);
    }
}
