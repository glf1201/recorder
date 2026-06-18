namespace RecorderApp.Models;

public sealed class FfmpegBootstrapResult
{
    public bool IsAvailable { get; init; }

    public string ExecutablePath { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}
