using System.Diagnostics;

namespace RecorderApp.Services;

public sealed class StartupRegistrationService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RecorderApp";
    private const string TrayStartupArgument = "--tray-startup";

    public void Apply(bool enabled, bool stopRunningWatchDogWhenDisabled = true)
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

            EnsureWatchDogRunning();
            return;
        }

        if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, false);
        }

        if (stopRunningWatchDogWhenDisabled)
        {
            StopRunningWatchDog();
        }
    }

    public void StopRunningWatchDog()
    {
        try
        {
            var watchDogPath = Path.Combine(AppContext.BaseDirectory, "WatchDog.exe");
            if (!File.Exists(watchDogPath))
            {
                return;
            }

            var processName = Path.GetFileNameWithoutExtension(watchDogPath);
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    var processPath = process.MainModule?.FileName;
                    if (!string.Equals(processPath, watchDogPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    process.Kill(true);
                    process.WaitForExit(3000);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static string? BuildStartupCommand()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var watchDogPath = Path.Combine(baseDirectory, "WatchDog.exe");
        var mainPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(mainPath))
        {
            return null;
        }

        return File.Exists(watchDogPath)
            ? $"{Quote(watchDogPath)} {Quote(mainPath)} {TrayStartupArgument}"
            : $"{Quote(mainPath)} {TrayStartupArgument}";
    }

    private static void EnsureWatchDogRunning()
    {
        try
        {
            var baseDirectory = AppContext.BaseDirectory;
            var watchDogPath = Path.Combine(baseDirectory, "WatchDog.exe");
            var mainExecutablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(mainExecutablePath) || !File.Exists(watchDogPath))
            {
                return;
            }

            var watchDogProcesses = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(watchDogPath));
            foreach (var process in watchDogProcesses)
            {
                try
                {
                    var processPath = process.MainModule?.FileName;
                    if (string.Equals(processPath, watchDogPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
                catch
                {
                }
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = watchDogPath,
                Arguments = Quote(mainExecutablePath),
                UseShellExecute = true,
                WorkingDirectory = baseDirectory,
            });
        }
        catch
        {
        }
    }

    private static string Quote(string value)
    {
        return '"' + value + '"';
    }
}
