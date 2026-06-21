namespace RecorderApp.Services;

public sealed class StartupRegistrationService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RecorderApp";
    private const string TrayStartupArgument = "--tray-startup";

    public void Apply(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryPath);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            var startupCommand = BuildStartupCommand();
            if (!string.IsNullOrWhiteSpace(startupCommand))
            {
                key.SetValue(ValueName, startupCommand);
            }

            return;
        }

        if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, false);
        }
    }

    private static string? BuildStartupCommand()
    {
        var mainPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(mainPath))
        {
            return null;
        }

        return $"{Quote(mainPath)} {TrayStartupArgument}";
    }

    private static string Quote(string value)
    {
        return '"' + value + '"';
    }
}
