using RecorderApp.ViewModels;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using FormsContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using FormsToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;
using FormsMouseButtons = System.Windows.Forms.MouseButtons;
using WpfMessageBox = System.Windows.MessageBox;

namespace RecorderApp.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly FormsNotifyIcon _notifyIcon;
    private readonly Icon _idleIcon;
    private readonly Icon _recordingIcon;
    private readonly Icon _recoveringIcon;
    private readonly Icon _errorIcon;
    private readonly DispatcherTimer _recordingBlinkTimer;
    private bool _allowRealClose;
    private bool _hasShownTrayHint;
    private bool _skipExitPasswordPrompt;
    private bool _showGrayRecordingIcon;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _idleIcon = CreateTrayIcon(Color.FromArgb(107, 114, 128));
        _recordingIcon = CreateTrayIcon(Color.FromArgb(220, 38, 38));
        _recoveringIcon = CreateTrayIcon(Color.FromArgb(217, 119, 6));
        _errorIcon = CreateTrayIcon(Color.FromArgb(124, 58, 237));
        _recordingBlinkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(850),
        };
        _recordingBlinkTimer.Tick += RecordingBlinkTimer_Tick;
        _notifyIcon = CreateNotifyIcon();
        UpdateTrayPresentation();
        UpdateRecordingBlinkPresentation();
        StateChanged += OnWindowStateChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowRealClose)
        {
            e.Cancel = true;
            HideToTray(showHint: true);
            return;
        }

        if (!_skipExitPasswordPrompt && _viewModel.HasExitPassword)
        {
            var dialog = new PasswordPromptWindow { Owner = this };
            var result = dialog.ShowDialog();
            if (result != true || !_viewModel.VerifyExitPassword(dialog.Password))
            {
                e.Cancel = true;
                _allowRealClose = false;
                if (result == true)
                {
                    WpfMessageBox.Show("退出密码错误。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _idleIcon.Dispose();
        _recordingIcon.Dispose();
        _recoveringIcon.Dispose();
        _errorIcon.Dispose();
        _viewModel.StopForApplicationExitAsync().GetAwaiter().GetResult();
        base.OnClosing(e);
    }

    private bool PromptForExitPassword()
    {
        if (_skipExitPasswordPrompt || !_viewModel.HasExitPassword)
        {
            return true;
        }

        var dialog = new PasswordPromptWindow { Owner = this };
        var result = dialog.ShowDialog();
        if (result != true)
        {
            return false;
        }

        if (_viewModel.VerifyExitPassword(dialog.Password))
        {
            return true;
        }

        WpfMessageBox.Show("退出密码错误。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsDialog();
    }

    private void HideToTrayButton_Click(object sender, RoutedEventArgs e)
    {
        HideToTray(showHint: true);
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray(showHint: true);
        }
    }

    private void ShowSettingsDialog()
    {
        var window = new SettingsWindow(_viewModel)
        {
            Owner = this,
        };
        window.ShowDialog();
    }

    private void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        if (!IsVisible)
        {
            Visibility = Visibility.Visible;
        }
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void HideToTray(bool showHint)
    {
        Hide();
        ShowInTaskbar = false;
        WindowState = WindowState.Minimized;

        if (showHint && !_hasShownTrayHint)
        {
            _notifyIcon.ShowBalloonTip(2500, "值守录屏软件正在后台运行", "已隐藏到系统托盘，可通过托盘图标恢复窗口或退出程序。", System.Windows.Forms.ToolTipIcon.Info);
            _hasShownTrayHint = true;
        }
    }

    private FormsNotifyIcon CreateNotifyIcon()
    {
        var notifyIcon = new FormsNotifyIcon
        {
            Icon = _idleIcon,
            Text = BuildTrayText(),
            Visible = true,
        };

        var contextMenu = new FormsContextMenuStrip();
        var showItem = new FormsToolStripMenuItem("显示主界面");
        showItem.Click += (_, _) => Dispatcher.Invoke(ShowFromTray);

        var settingsItem = new FormsToolStripMenuItem("打开设置");
        settingsItem.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            ShowFromTray();
            ShowSettingsDialog();
        });

        var openRecordsItem = new FormsToolStripMenuItem("打开录像目录");
        openRecordsItem.Click += (_, _) => Dispatcher.Invoke(() => _viewModel.OpenRecordFolderCommand.Execute(null));

        var openLogsItem = new FormsToolStripMenuItem("打开日志目录");
        openLogsItem.Click += (_, _) => Dispatcher.Invoke(() => _viewModel.OpenLogsFolderCommand.Execute(null));

        var startItem = new FormsToolStripMenuItem("开始录制");
        startItem.Click += async (_, _) => await Dispatcher.InvokeAsync(async () => await _viewModel.StartRecordingAsync());

        var stopItem = new FormsToolStripMenuItem("停止录制");
        stopItem.Click += async (_, _) => await Dispatcher.InvokeAsync(async () => await _viewModel.StopRecordingAsync());

        var refreshItem = new FormsToolStripMenuItem("刷新状态");
        refreshItem.Click += (_, _) => Dispatcher.Invoke(() => _viewModel.RefreshDashboard());

        var exitItem = new FormsToolStripMenuItem("退出程序");
        exitItem.Click += (_, _) => Dispatcher.Invoke(async () => await ExitApplicationAsync());

        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(openRecordsItem);
        contextMenu.Items.Add(openLogsItem);
        contextMenu.Items.Add(startItem);
        contextMenu.Items.Add(stopItem);
        contextMenu.Items.Add(refreshItem);
        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        notifyIcon.ContextMenuStrip = contextMenu;
        notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == FormsMouseButtons.Left)
            {
                Dispatcher.Invoke(ShowFromTray);
            }
        };

        return notifyIcon;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.StatusText) or nameof(MainViewModel.StatusDisplayName) or nameof(MainViewModel.StatusSubtitle))
        {
            Dispatcher.Invoke(UpdateTrayPresentation);
        }

        if (e.PropertyName is nameof(MainViewModel.IsRecording) or nameof(MainViewModel.StatusText))
        {
            Dispatcher.Invoke(UpdateRecordingBlinkPresentation);
        }

        if (e.PropertyName == nameof(MainViewModel.IsStopping) && _viewModel.IsStopping)
        {
            Dispatcher.Invoke(() =>
            {
                _notifyIcon.ShowBalloonTip(1800, "录屏软件", "正在停止录屏...", System.Windows.Forms.ToolTipIcon.Info);
            });
        }
    }

    private void UpdateTrayPresentation()
    {
        _notifyIcon.Icon = _viewModel.StatusText switch
        {
            "Recording" => _recordingIcon,
            "Recovering" => _recoveringIcon,
            "Waiting" => _recordingIcon,
            "Error" => _errorIcon,
            _ => _idleIcon,
        };
        _notifyIcon.Text = BuildTrayText();
    }

    private string BuildTrayText()
    {
        var text = $"录屏软件 - {_viewModel.StatusDisplayName}";
        text = text.Replace("录屏软件", "值守录屏软件");
        return text.Length > 60 ? text[..60] : text;
    }

    private async Task ExitApplicationAsync()
    {
        if (!PromptForExitPassword())
        {
            return;
        }

        _allowRealClose = true;
        _skipExitPasswordPrompt = true;
        await _viewModel.StopForApplicationExitAsync();
        Close();
    }

    public void PrepareForSystemShutdown()
    {
        _allowRealClose = true;
        _skipExitPasswordPrompt = true;
        if (_viewModel.IsRecording)
        {
            _notifyIcon.ShowBalloonTip(1800, "录屏软件", "正在停止录屏...", System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    public void PrepareForTrayOnlyStartup()
    {
        ShowInTaskbar = false;
        WindowState = WindowState.Minimized;
        if (IsVisible)
        {
            HideToTray(showHint: false);
        }
    }

    public void BringToFrontFromExternalLaunch()
    {
        ShowFromTray();
    }

    private void RecordingBlinkTimer_Tick(object? sender, EventArgs e)
    {
        _showGrayRecordingIcon = !_showGrayRecordingIcon;
        UpdateRecordingIconVisibility();
    }

    private void UpdateRecordingBlinkPresentation()
    {
        var shouldBlink = _viewModel.StatusText == "Recording";
        if (!shouldBlink)
        {
            _recordingBlinkTimer.Stop();
            _showGrayRecordingIcon = false;
            UpdateRecordingIconVisibility();
            return;
        }

        if (!_recordingBlinkTimer.IsEnabled)
        {
            _showGrayRecordingIcon = false;
            UpdateRecordingIconVisibility();
            _recordingBlinkTimer.Start();
        }
    }

    private void UpdateRecordingIconVisibility()
    {
        if (RecordingRedIcon is null || RecordingRedCenter is null || RecordingGrayIcon is null || RecordingGrayCenter is null)
        {
            return;
        }

        var redVisibility = _showGrayRecordingIcon ? Visibility.Collapsed : Visibility.Visible;
        var grayVisibility = _showGrayRecordingIcon ? Visibility.Visible : Visibility.Collapsed;
        RecordingRedIcon.Visibility = redVisibility;
        RecordingRedCenter.Visibility = redVisibility;
        RecordingGrayIcon.Visibility = grayVisibility;
        RecordingGrayCenter.Visibility = grayVisibility;
    }

    private static Icon CreateTrayIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var shadowBrush = new SolidBrush(Color.FromArgb(40, 15, 23, 42));
            graphics.FillEllipse(shadowBrush, 5, 6, 22, 22);

            using var fillBrush = new SolidBrush(color);
            graphics.FillEllipse(fillBrush, 4, 4, 22, 22);

            using var ringPen = new Pen(Color.FromArgb(230, 255, 255, 255), 2f);
            graphics.DrawEllipse(ringPen, 4, 4, 22, 22);

            using var centerBrush = new SolidBrush(Color.White);
            graphics.FillEllipse(centerBrush, 10, 10, 10, 10);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
