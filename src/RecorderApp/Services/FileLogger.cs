namespace RecorderApp.Services;

public sealed class FileLogger
{
    private readonly object _syncRoot = new();

    public FileLogger()
    {
        Directory.CreateDirectory(AppPaths.LogDirectory);
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Error(string message, Exception exception) => Write("ERROR", $"{message}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        var filePath = Path.Combine(AppPaths.LogDirectory, $"recorder-{DateTime.Now:yyyy-MM-dd}.log");

        lock (_syncRoot)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                    writer.WriteLine(line);
                    writer.Flush();
                    return;
                }
                catch (IOException) when (attempt < 2)
                {
                    Thread.Sleep(25);
                }
                catch (UnauthorizedAccessException) when (attempt < 2)
                {
                    Thread.Sleep(25);
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"FileLogger failed: {exception}");
                    return;
                }
            }
        }
    }
}
