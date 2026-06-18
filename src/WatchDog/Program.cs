using System.Diagnostics;
using System.Globalization;
using System.Text;

var baseDirectory = AppContext.BaseDirectory;
var (mainExecutablePath, forwardedArguments) = ResolveLaunchArguments(args, baseDirectory);
var heartbeatFile = Path.Combine(baseDirectory, "Data", "heartbeat.txt");
var logDirectory = Path.Combine(baseDirectory, "Logs");
Directory.CreateDirectory(logDirectory);

while (true)
{
    try
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(mainExecutablePath));
        if (processes.Length == 0)
        {
            StartMainProcess(mainExecutablePath, forwardedArguments, logDirectory, "Main process not found, restarting.");
        }
        else if (IsHeartbeatExpired(heartbeatFile))
        {
            foreach (var process in processes)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                }
            }

            StartMainProcess(mainExecutablePath, forwardedArguments, logDirectory, "Heartbeat expired, restarting main process.");
        }
    }
    catch (Exception exception)
    {
        File.AppendAllText(
            Path.Combine(logDirectory, $"watchdog-{DateTime.Now:yyyy-MM-dd}.log"),
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}",
            Encoding.UTF8);
    }

    Thread.Sleep(TimeSpan.FromSeconds(5));
}

static bool IsHeartbeatExpired(string heartbeatFile)
{
    if (!File.Exists(heartbeatFile))
    {
        return false;
    }

    var content = File.ReadAllText(heartbeatFile, Encoding.UTF8).Trim();
    return DateTime.TryParse(content, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var heartbeatUtc)
        && DateTime.UtcNow - heartbeatUtc > TimeSpan.FromSeconds(60);
}

static (string MainExecutablePath, string[] ForwardedArguments) ResolveLaunchArguments(string[] args, string baseDirectory)
{
    if (args.Length == 0)
    {
        return (Path.Combine(baseDirectory, "RecorderApp.exe"), Array.Empty<string>());
    }

    var candidatePath = args[0];
    if (!string.IsNullOrWhiteSpace(candidatePath)
        && (Path.IsPathRooted(candidatePath) || candidatePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
    {
        return (candidatePath, args.Skip(1).ToArray());
    }

    return (Path.Combine(baseDirectory, "RecorderApp.exe"), args);
}

static void StartMainProcess(string executablePath, string[] forwardedArguments, string logDirectory, string message)
{
    File.AppendAllText(
        Path.Combine(logDirectory, $"watchdog-{DateTime.Now:yyyy-MM-dd}.log"),
        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}",
        Encoding.UTF8);

    Process.Start(new ProcessStartInfo
    {
        FileName = executablePath,
        Arguments = string.Join(" ", forwardedArguments.Select(QuoteArgument)),
        UseShellExecute = true,
        WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
    });
}

static string QuoteArgument(string argument)
{
    if (string.IsNullOrWhiteSpace(argument))
    {
        return "\"\"";
    }

    return argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument;
}
