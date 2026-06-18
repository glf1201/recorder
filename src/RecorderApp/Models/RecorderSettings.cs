namespace RecorderApp.Models;

public sealed class RecorderSettings : RecorderApp.Infrastructure.ObservableObject
{
    private string _storagePath = string.Empty;
    private string _cleanupDirectory1 = string.Empty;
    private string _cleanupDirectory2 = string.Empty;
    private string _cleanupDirectory3 = string.Empty;
    private string _cleanupDirectory4 = string.Empty;
    private string _cleanupDirectory5 = string.Empty;
    private int _quality = 28;
    private int _frameRate = 5;
    private string _preferredCodec = "H264";
    private string _container = "mkv";
    private string _audioDevice = "default";
    private int _retentionDays = 7;
    private string _cleanupTime = "15:00";
    private bool _autoStartWithWindows = true;
    private bool _autoStartRecording = true;
    private string _exitPassword = string.Empty;
    private string _displayTarget = "AllDisplays";

    public string StoragePath
    {
        get => _storagePath;
        set => SetProperty(ref _storagePath, value);
    }

    public string CleanupDirectory1
    {
        get => _cleanupDirectory1;
        set => SetProperty(ref _cleanupDirectory1, value);
    }

    public string CleanupDirectory2
    {
        get => _cleanupDirectory2;
        set => SetProperty(ref _cleanupDirectory2, value);
    }

    public string CleanupDirectory3
    {
        get => _cleanupDirectory3;
        set => SetProperty(ref _cleanupDirectory3, value);
    }

    public string CleanupDirectory4
    {
        get => _cleanupDirectory4;
        set => SetProperty(ref _cleanupDirectory4, value);
    }

    public string CleanupDirectory5
    {
        get => _cleanupDirectory5;
        set => SetProperty(ref _cleanupDirectory5, value);
    }

    public int Quality
    {
        get => _quality;
        set => SetProperty(ref _quality, value);
    }

    public int FrameRate
    {
        get => _frameRate;
        set => SetProperty(ref _frameRate, value);
    }

    public string PreferredCodec
    {
        get => _preferredCodec;
        set => SetProperty(ref _preferredCodec, value);
    }

    public string Container
    {
        get => _container;
        set => SetProperty(ref _container, value);
    }

    public string AudioDevice
    {
        get => _audioDevice;
        set => SetProperty(ref _audioDevice, value);
    }

    public int RetentionDays
    {
        get => _retentionDays;
        set => SetProperty(ref _retentionDays, value);
    }

    public string CleanupTime
    {
        get => _cleanupTime;
        set => SetProperty(ref _cleanupTime, value);
    }

    public bool AutoStartWithWindows
    {
        get => _autoStartWithWindows;
        set => SetProperty(ref _autoStartWithWindows, value);
    }

    public bool AutoStartRecording
    {
        get => _autoStartRecording;
        set => SetProperty(ref _autoStartRecording, value);
    }

    public string ExitPassword
    {
        get => _exitPassword;
        set => SetProperty(ref _exitPassword, value);
    }

    public string DisplayTarget
    {
        get => _displayTarget;
        set => SetProperty(ref _displayTarget, value);
    }

    public RecorderSettings Clone()
    {
        return new RecorderSettings
        {
            StoragePath = StoragePath,
            CleanupDirectory1 = CleanupDirectory1,
            CleanupDirectory2 = CleanupDirectory2,
            CleanupDirectory3 = CleanupDirectory3,
            CleanupDirectory4 = CleanupDirectory4,
            CleanupDirectory5 = CleanupDirectory5,
            Quality = Quality,
            FrameRate = FrameRate,
            PreferredCodec = PreferredCodec,
            Container = Container,
            AudioDevice = AudioDevice,
            RetentionDays = RetentionDays,
            CleanupTime = CleanupTime,
            AutoStartWithWindows = AutoStartWithWindows,
            AutoStartRecording = AutoStartRecording,
            ExitPassword = ExitPassword,
            DisplayTarget = DisplayTarget,
        };
    }

    public void CopyFrom(RecorderSettings other)
    {
        StoragePath = other.StoragePath;
        CleanupDirectory1 = other.CleanupDirectory1;
        CleanupDirectory2 = other.CleanupDirectory2;
        CleanupDirectory3 = other.CleanupDirectory3;
        CleanupDirectory4 = other.CleanupDirectory4;
        CleanupDirectory5 = other.CleanupDirectory5;
        Quality = other.Quality;
        FrameRate = other.FrameRate;
        PreferredCodec = other.PreferredCodec;
        Container = other.Container;
        AudioDevice = other.AudioDevice;
        RetentionDays = other.RetentionDays;
        CleanupTime = other.CleanupTime;
        AutoStartWithWindows = other.AutoStartWithWindows;
        AutoStartRecording = other.AutoStartRecording;
        ExitPassword = other.ExitPassword;
        DisplayTarget = other.DisplayTarget;
    }

    public IEnumerable<string> GetAdditionalCleanupDirectories()
    {
        yield return CleanupDirectory1;
        yield return CleanupDirectory2;
        yield return CleanupDirectory3;
        yield return CleanupDirectory4;
        yield return CleanupDirectory5;
    }
}
