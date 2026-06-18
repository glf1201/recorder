namespace RecorderApp.Models;

public sealed class RecordingStatusSnapshot
{
    public string State { get; init; } = "Idle";

    public string Message { get; init; } = string.Empty;

    public string CurrentFile { get; init; } = string.Empty;

    public string Encoder { get; init; } = string.Empty;

    public string DiskStatus { get; init; } = string.Empty;
}
