using RecorderApp.ViewModels;
using WpfMessageBox = System.Windows.MessageBox;

namespace RecorderApp.Views;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly RecorderApp.Models.RecorderSettings _snapshot;
    private bool _confirmed;

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _snapshot = viewModel.Settings.Clone();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ExitPasswordBox.Password = string.Empty;
        ConfirmExitPasswordBox.Password = string.Empty;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var newPassword = ExitPasswordBox.Password;
        var confirmPassword = ConfirmExitPasswordBox.Password;

        if (!string.IsNullOrWhiteSpace(newPassword) || !string.IsNullOrWhiteSpace(confirmPassword))
        {
            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            {
                WpfMessageBox.Show("两次输入的退出密码不一致。", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _viewModel.Settings.ExitPassword = newPassword;
        }

        if (!_viewModel.TrySaveSettingsForDialog(out var error))
        {
            WpfMessageBox.Show(error, "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _confirmed = true;
        DialogResult = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_confirmed)
        {
            _viewModel.Settings.CopyFrom(_snapshot);
        }

        base.OnClosing(e);
    }
}
