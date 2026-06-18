using RecorderApp.Infrastructure;
using RecorderApp.Models;
using RecorderApp.Services;
using FormsFolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using Screen = System.Windows.Forms.Screen;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using WpfBrush = System.Windows.Media.Brush;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfColor = System.Windows.Media.Color;
using System.ComponentModel;

namespace RecorderApp.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly WpfBrush RecordingBrush = CreateBrush("#2563EB");
    private static readonly WpfBrush SuccessBrush = CreateBrush("#16A34A");
    private static readonly WpfBrush WarningBrush = CreateBrush("#D97706");
    private static readonly WpfBrush DangerBrush = CreateBrush("#DC2626");
    private static readonly WpfBrush IdleBrush = CreateBrush("#6B7280");

    private readonly JsonSettingsStore _settingsStore;
    private readonly StartupRegistrationService _startupRegistrationService;
    private readonly RecordingCoordinator _recordingCoordinator;
    private readonly AudioDeviceService _audioDeviceService;
    private readonly FFmpegBootstrapper _ffmpegBootstrapper;
    private readonly FileLogger _logger;
    private string _statusText = "Idle";
    private string _currentFile = string.Empty;
    private string _lastMessage = string.Empty;
    private string _diskStatus = string.Empty;
    private string _encoder = string.Empty;
    private string _environmentStatus = string.Empty;
    private string _diskCapacitySummary = "-- / --";
    private string _diskDriveName = "--";
    private bool _isRecording;
    private bool _isStopping;

    public MainViewModel(
        RecorderSettings settings,
        JsonSettingsStore settingsStore,
        StartupRegistrationService startupRegistrationService,
        RecordingCoordinator recordingCoordinator,
        AudioDeviceService audioDeviceService,
        FFmpegBootstrapper ffmpegBootstrapper,
        string initialEnvironmentStatus,
        FileLogger logger)
    {
        Settings = settings;
        _settingsStore = settingsStore;
        _startupRegistrationService = startupRegistrationService;
        _recordingCoordinator = recordingCoordinator;
        _audioDeviceService = audioDeviceService;
        _ffmpegBootstrapper = ffmpegBootstrapper;
        _logger = logger;
        DisplayTargets = new ObservableCollection<string>();
        AudioDevices = new ObservableCollection<AudioDeviceOption>();
        PreferredCodecs = new ObservableCollection<string>(new[] { "H264", "H265" });
        Containers = new ObservableCollection<string>(new[] { "mkv", "mp4" });
        RecentRecordings = new ObservableCollection<DashboardItem>();
        RecentLogs = new ObservableCollection<DashboardItem>();

        StartRecordingCommand = new AsyncRelayCommand(StartRecordingAsync, () => !IsRecording);
        StopRecordingCommand = new AsyncRelayCommand(StopRecordingAsync, () => IsRecording);
        SaveSettingsCommand = new RelayCommand(SaveSettingsFromUi);
        OpenRecordFolderCommand = new RelayCommand(OpenRecordFolder);
        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);
        RefreshAudioDevicesCommand = new RelayCommand(LoadAudioDevices);
        BootstrapFfmpegCommand = new AsyncRelayCommand(BootstrapFfmpegAsync);
        RefreshDashboardCommand = new RelayCommand(RefreshDashboard);
        OpenPathCommand = new RelayCommand<string>(OpenPath);
        BrowseStoragePathCommand = new RelayCommand(BrowseStoragePath);
        BrowseCleanupDirectoryCommand = new RelayCommand<string>(BrowseCleanupDirectory);

        _recordingCoordinator.StatusChanged += OnRecordingStatusChanged;
        LoadDisplayTargets();
        LoadAudioDevices();
        EnvironmentStatus = initialEnvironmentStatus;
        RefreshDashboard();
        RefreshDiskMetrics();
        Settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    public RecorderSettings Settings { get; }

    public ObservableCollection<string> DisplayTargets { get; }

    public ObservableCollection<AudioDeviceOption> AudioDevices { get; }

    public ObservableCollection<string> PreferredCodecs { get; }

    public ObservableCollection<string> Containers { get; }

    public ObservableCollection<DashboardItem> RecentRecordings { get; }

    public ObservableCollection<DashboardItem> RecentLogs { get; }

    public AsyncRelayCommand StartRecordingCommand { get; }

    public AsyncRelayCommand StopRecordingCommand { get; }

    public RelayCommand SaveSettingsCommand { get; }

    public RelayCommand OpenRecordFolderCommand { get; }

    public RelayCommand OpenLogsFolderCommand { get; }

    public RelayCommand RefreshAudioDevicesCommand { get; }

    public AsyncRelayCommand BootstrapFfmpegCommand { get; }

    public RelayCommand RefreshDashboardCommand { get; }

    public RelayCommand<string> OpenPathCommand { get; }

    public RelayCommand BrowseStoragePathCommand { get; }

    public RelayCommand<string> BrowseCleanupDirectoryCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string CurrentFile
    {
        get => _currentFile;
        private set => SetProperty(ref _currentFile, value);
    }

    public string LastMessage
    {
        get => _lastMessage;
        private set => SetProperty(ref _lastMessage, value);
    }

    public string DiskStatus
    {
        get => _diskStatus;
        private set => SetProperty(ref _diskStatus, value);
    }

    public string Encoder
    {
        get => _encoder;
        private set => SetProperty(ref _encoder, value);
    }

    public string EnvironmentStatus
    {
        get => _environmentStatus;
        private set
        {
            if (SetProperty(ref _environmentStatus, value))
            {
                RaisePropertyChanged(nameof(EnvironmentStatusShort));
            }
        }
    }

    public string DiskCapacitySummary
    {
        get => _diskCapacitySummary;
        private set => SetProperty(ref _diskCapacitySummary, value);
    }

    public string DiskDriveName
    {
        get => _diskDriveName;
        private set => SetProperty(ref _diskDriveName, value);
    }

    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (SetProperty(ref _isRecording, value))
            {
                StartRecordingCommand.NotifyCanExecuteChanged();
                StopRecordingCommand.NotifyCanExecuteChanged();
                RaiseStatusPresentationProperties();
            }
        }
    }

    public bool HasExitPassword => !string.IsNullOrWhiteSpace(Settings.ExitPassword);

    public bool IsStopping
    {
        get => _isStopping;
        private set => SetProperty(ref _isStopping, value);
    }

    public string StatusDisplayName => StatusText switch
    {
        "Recording" => "正在录屏",
        "Recovering" => "恢复中",
        "Waiting" => "等待下一段",
        "Stopped" => "已停止",
        "Error" => "异常",
        _ => "未开始",
    };

    public string StartCardTitle => IsRecording ? "正在录屏" : "开始录屏";

    public string StatusSubtitle => string.IsNullOrWhiteSpace(LastMessage)
        ? IsRecording ? "录制任务正在后台持续运行" : "准备开始无人值守录制"
        : LastMessage;

    public string StatusGlyph => StatusText switch
    {
        "Recording" => "\uE768",
        "Recovering" => "\uE895",
        "Waiting" => "\uE823",
        "Stopped" => "\uE71A",
        "Error" => "\uEA39",
        _ => "\uE73E",
    };

    public WpfBrush StatusBrush => StatusText switch
    {
        "Recording" => RecordingBrush,
        "Recovering" => WarningBrush,
        "Waiting" => SuccessBrush,
        "Stopped" => IdleBrush,
        "Error" => DangerBrush,
        _ => IdleBrush,
    };

    public string CurrentFileName => string.IsNullOrWhiteSpace(CurrentFile) ? "暂无活动文件" : Path.GetFileName(CurrentFile);

    public string EnvironmentStatusShort => string.IsNullOrWhiteSpace(EnvironmentStatus)
        ? "待检查"
        : EnvironmentStatus.Length > 32 ? EnvironmentStatus[..32] + "..." : EnvironmentStatus;

    public bool ValidateSettings(out string error)
    {
        if (string.IsNullOrWhiteSpace(Settings.StoragePath))
        {
            error = "保存路径不能为空。";
            return false;
        }

        if (!ValidateAdditionalCleanupDirectories(out error))
        {
            return false;
        }

        if (Settings.Quality is < 1 or > 30)
        {
            error = "视频质量必须在 1 到 30 之间。";
            return false;
        }

        if (Settings.FrameRate is < 2 or > 30)
        {
            error = "帧率必须在 2 到 30 之间。";
            return false;
        }

        if (Settings.RetentionDays is < 3 or > 30)
        {
            error = "保留天数必须在 3 到 30 天之间。";
            return false;
        }

        if (!TimeSpan.TryParseExact(Settings.CleanupTime, @"hh\:mm", CultureInfo.InvariantCulture, out _))
        {
            error = "清理时间格式必须是 HH:mm。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public async Task StartRecordingAsync()
    {
        if (!TryPersistSettings(out var error, showSuccessMessage: false))
        {
            WpfMessageBox.Show(error, "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsStopping = false;
        await _recordingCoordinator.StartAsync(Settings);
        IsRecording = true;
        RefreshDashboard();
        RefreshDiskMetrics();
    }

    public async Task StopRecordingAsync()
    {
        IsStopping = true;
        try
        {
            await _recordingCoordinator.StopAsync("manual-stop");
            IsRecording = false;
            RefreshDashboard();
            RefreshDiskMetrics();
        }
        finally
        {
            IsStopping = false;
        }
    }

    public async Task StopForApplicationExitAsync()
    {
        if (IsRecording)
        {
            IsStopping = true;
            try
            {
                await _recordingCoordinator.StopAsync("application-exit");
                IsRecording = false;
                RefreshDashboard();
                RefreshDiskMetrics();
            }
            finally
            {
                IsStopping = false;
            }
        }
    }

    public async Task StopForSystemShutdownAsync()
    {
        if (IsRecording)
        {
            IsStopping = true;
            try
            {
                await _recordingCoordinator.StopAsync("windows-session-ending");
                IsRecording = false;
                RefreshDashboard();
                RefreshDiskMetrics();
            }
            finally
            {
                IsStopping = false;
            }
        }
    }

    public bool VerifyExitPassword(string password)
    {
        return string.Equals(Settings.ExitPassword ?? string.Empty, password ?? string.Empty, StringComparison.Ordinal);
    }

    public bool TrySaveSettingsForDialog(out string error)
    {
        return TryPersistSettings(out error, showSuccessMessage: false);
    }

    public void RefreshDashboard()
    {
        RefreshRecentRecordings();
        RefreshRecentLogs();
    }

    private void SaveSettingsFromUi()
    {
        if (!TryPersistSettings(out var error, showSuccessMessage: true))
        {
            WpfMessageBox.Show(error, "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RefreshDashboard();
        RefreshDiskMetrics();
    }

    private bool TryPersistSettings(out string error, bool showSuccessMessage)
    {
        if (!ValidateSettings(out error))
        {
            return false;
        }

        AppPaths.EnsureDirectories(Settings.StoragePath);
        _settingsStore.Save(Settings);
        _startupRegistrationService.Apply(Settings.AutoStartWithWindows);
        _logger.Info("Settings saved.");

        if (showSuccessMessage)
        {
            WpfMessageBox.Show("设置已保存。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        RefreshDiskMetrics();
        return true;
    }

    private void OpenRecordFolder()
    {
        AppPaths.EnsureDirectories(Settings.StoragePath);
        Process.Start(new ProcessStartInfo
        {
            FileName = Settings.StoragePath,
            UseShellExecute = true,
        });
    }

    private void OpenLogsFolder()
    {
        Directory.CreateDirectory(AppPaths.LogDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.LogDirectory,
            UseShellExecute = true,
        });
    }

    private void OpenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var targetPath = path;
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = targetPath,
            UseShellExecute = true,
        });
    }

    private void BrowseStoragePath()
    {
        using var dialog = new FormsFolderBrowserDialog
        {
            Description = "请选择录像保存目录",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(Settings.StoragePath) ? Settings.StoragePath : AppPaths.DefaultRecordDirectory,
            ShowNewFolderButton = true,
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            Settings.StoragePath = dialog.SelectedPath;
        }
    }

    private void BrowseCleanupDirectory(string? slotValue)
    {
        if (!int.TryParse(slotValue, out var slot))
        {
            return;
        }

        if (slot is < 1 or > 5)
        {
            return;
        }

        var currentPath = GetCleanupDirectory(slot);
        using var dialog = new FormsFolderBrowserDialog
        {
            Description = $"请选择定时删除目录 {slot}",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(currentPath) ? currentPath : Settings.StoragePath,
            ShowNewFolderButton = true,
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            SetCleanupDirectory(slot, dialog.SelectedPath);
        }
    }

    private void LoadDisplayTargets()
    {
        DisplayTargets.Clear();
        DisplayTargets.Add("AllDisplays");
        foreach (var screen in Screen.AllScreens)
        {
            DisplayTargets.Add(screen.DeviceName);
        }

        if (!DisplayTargets.Contains(Settings.DisplayTarget))
        {
            Settings.DisplayTarget = "AllDisplays";
        }
    }

    private void LoadAudioDevices()
    {
        AudioDevices.Clear();
        foreach (var device in _audioDeviceService.GetPlaybackDevices())
        {
            AudioDevices.Add(device);
        }

        if (string.IsNullOrWhiteSpace(Settings.AudioDevice) || string.Equals(Settings.AudioDevice, "default", StringComparison.OrdinalIgnoreCase))
        {
            Settings.AudioDevice = AudioDevices.FirstOrDefault(device => device.IsDefault)?.InputName
                ?? AudioDevices.FirstOrDefault()?.InputName
                ?? string.Empty;
            return;
        }

        if (!AudioDevices.Any(device => string.Equals(device.InputName, Settings.AudioDevice, StringComparison.OrdinalIgnoreCase)))
        {
            Settings.AudioDevice = AudioDevices.FirstOrDefault(device => device.IsDefault)?.InputName
                ?? AudioDevices.FirstOrDefault()?.InputName
                ?? string.Empty;
        }
    }

    private async Task BootstrapFfmpegAsync()
    {
        EnvironmentStatus = "正在检查 FFmpeg...";
        var result = await _ffmpegBootstrapper.EnsureAvailableAsync(CancellationToken.None);
        EnvironmentStatus = result.Message;
        LoadAudioDevices();
        RefreshDashboard();

        WpfMessageBox.Show(
            result.Message,
            result.IsAvailable ? "FFmpeg 已就绪" : "FFmpeg 未就绪",
            MessageBoxButton.OK,
            result.IsAvailable ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void OnRecordingStatusChanged(object? sender, RecordingStatusSnapshot snapshot)
    {
        WpfApplication.Current.Dispatcher.Invoke(() =>
        {
            StatusText = snapshot.State;
            LastMessage = snapshot.Message;
            CurrentFile = snapshot.CurrentFile;
            Encoder = snapshot.Encoder;
            DiskStatus = snapshot.DiskStatus;
            IsRecording = string.Equals(snapshot.State, "Recording", StringComparison.OrdinalIgnoreCase)
                || string.Equals(snapshot.State, "Recovering", StringComparison.OrdinalIgnoreCase)
                || string.Equals(snapshot.State, "Waiting", StringComparison.OrdinalIgnoreCase);
            RaiseStatusPresentationProperties();
            RaisePropertyChanged(nameof(CurrentFileName));
            RefreshDashboard();
            RefreshDiskMetrics();
        });
    }

    private void RefreshDiskMetrics()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(Settings.StoragePath));
            if (string.IsNullOrWhiteSpace(root))
            {
                DiskDriveName = "--";
                DiskCapacitySummary = "-- / --";
                return;
            }

            var drive = new DriveInfo(root);
            var free = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
            var total = drive.TotalSize / 1024d / 1024d / 1024d;
            DiskDriveName = drive.Name.TrimEnd('\\');
            DiskCapacitySummary = $"{free:0.0}G / {total:0.0}G";
        }
        catch
        {
            DiskDriveName = "--";
            DiskCapacitySummary = "-- / --";
        }
    }

    private void RefreshRecentRecordings()
    {
        RecentRecordings.Clear();

        if (!Directory.Exists(Settings.StoragePath))
        {
            RecentRecordings.Add(new DashboardItem
            {
                Glyph = "\uE7C3",
                Title = "暂无录像文件",
                Subtitle = "当前录制目录还没有生成录像。",
                FullPath = Settings.StoragePath,
            });
            return;
        }

        var files = Directory.EnumerateFiles(Settings.StoragePath, "*.*", SearchOption.AllDirectories)
            .Where(file => string.Equals(Path.GetExtension(file), ".mkv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetExtension(file), ".mp4", StringComparison.OrdinalIgnoreCase))
            .Select(file => new FileInfo(file))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Take(5)
            .ToList();

        if (files.Count == 0)
        {
            RecentRecordings.Add(new DashboardItem
            {
                Glyph = "\uE7C3",
                Title = "暂无录像文件",
                Subtitle = "录制开始后这里会显示最新片段。",
                FullPath = Settings.StoragePath,
            });
            return;
        }

        foreach (var file in files)
        {
            RecentRecordings.Add(new DashboardItem
            {
                Glyph = "\uE8B7",
                Title = file.Name,
                Subtitle = $"{file.Directory?.Name} | {file.LastWriteTime:MM-dd HH:mm} | {FormatFileSize(file.Length)}",
                FullPath = file.FullName,
            });
        }
    }

    private void RefreshRecentLogs()
    {
        RecentLogs.Clear();
        Directory.CreateDirectory(AppPaths.LogDirectory);

        var logFiles = Directory.EnumerateFiles(AppPaths.LogDirectory, "*.log", SearchOption.TopDirectoryOnly)
            .Select(file => new FileInfo(file))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ToList();

        if (logFiles.Count == 0)
        {
            RecentLogs.Add(new DashboardItem
            {
                Glyph = "\uE9D2",
                Title = "暂无日志",
                Subtitle = "程序运行后将在这里显示最新日志。",
                FullPath = AppPaths.LogDirectory,
            });
            return;
        }

        foreach (var line in ReadLatestLogEntries(logFiles.First().FullName, 5))
        {
            RecentLogs.Add(new DashboardItem
            {
                Glyph = "\uE9D2",
                Title = line.Length > 90 ? line[..90] + "..." : line,
                Subtitle = Path.GetFileName(logFiles.First().FullName),
                FullPath = logFiles.First().FullName,
            });
        }
    }

    private static IEnumerable<string> ReadLatestLogEntries(string filePath, int count)
    {
        var lines = File.ReadAllLines(filePath, Encoding.UTF8)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(count)
            .Reverse()
            .ToList();

        return lines.Count == 0 ? new[] { "日志文件为空。" } : lines;
    }

    private void RaiseStatusPresentationProperties()
    {
        RaisePropertyChanged(nameof(StatusDisplayName));
        RaisePropertyChanged(nameof(StatusSubtitle));
        RaisePropertyChanged(nameof(StatusGlyph));
        RaisePropertyChanged(nameof(StatusBrush));
        RaisePropertyChanged(nameof(StartCardTitle));
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecorderSettings.StoragePath))
        {
            RefreshDiskMetrics();
        }
        else if (e.PropertyName == nameof(RecorderSettings.ExitPassword))
        {
            RaisePropertyChanged(nameof(HasExitPassword));
        }
    }

    private static WpfBrush CreateBrush(string hex)
    {
        return new WpfSolidColorBrush((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex)!);
    }

    private bool ValidateAdditionalCleanupDirectories(out string error)
    {
        error = string.Empty;

        var recordingPath = NormalizePath(Settings.StoragePath);
        var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Settings.GetAdditionalCleanupDirectories())
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var normalizedPath = NormalizePath(directory);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                error = "定时删除目录格式无效。";
                return false;
            }

            if (string.Equals(normalizedPath, recordingPath, StringComparison.OrdinalIgnoreCase))
            {
                error = "录像目录已经在基本设置中配置，不需要在定时删除里重复填写。";
                return false;
            }

            if (!uniquePaths.Add(normalizedPath))
            {
                error = "定时删除目录中存在重复项，请检查后再保存。";
                return false;
            }
        }

        return true;
    }

    private string GetCleanupDirectory(int slot)
    {
        return slot switch
        {
            1 => Settings.CleanupDirectory1,
            2 => Settings.CleanupDirectory2,
            3 => Settings.CleanupDirectory3,
            4 => Settings.CleanupDirectory4,
            5 => Settings.CleanupDirectory5,
            _ => string.Empty,
        };
    }

    private void SetCleanupDirectory(int slot, string path)
    {
        switch (slot)
        {
            case 1:
                Settings.CleanupDirectory1 = path;
                break;
            case 2:
                Settings.CleanupDirectory2 = path;
                break;
            case 3:
                Settings.CleanupDirectory3 = path;
                break;
            case 4:
                Settings.CleanupDirectory4 = path;
                break;
            case 5:
                Settings.CleanupDirectory5 = path;
                break;
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

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var index = 0;

        while (size >= 1024 && index < units.Length - 1)
        {
            size /= 1024;
            index++;
        }

        return $"{size:0.##} {units[index]}";
    }
}
