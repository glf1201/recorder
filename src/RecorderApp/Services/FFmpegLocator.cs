namespace RecorderApp.Services;

public sealed class FFmpegLocator
{
    public string? Locate()
    {
        foreach (var candidate in EnumerateCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        yield return AppPaths.FfmpegBundledPath;
        yield return Path.Combine(Environment.CurrentDirectory, "ffmpeg", "bin", "ffmpeg.exe");
        yield return Path.Combine(Environment.CurrentDirectory, "ffmpeg.exe");
        yield return @"C:\record\ffmpeg\bin\ffmpeg.exe";
        yield return Path.Combine(AppPaths.BaseDirectory, "ffmpeg.exe");
        yield return @"C:\Program Files\ffmpeg\bin\ffmpeg.exe";
        yield return @"C:\ffmpeg\bin\ffmpeg.exe";

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var path in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(path, "ffmpeg.exe");
        }
    }
}
