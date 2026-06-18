namespace RecorderApp.Models;

public sealed class AudioDeviceOption
{
    public string DisplayName { get; init; } = string.Empty;

    public string InputName { get; init; } = string.Empty;

    public bool IsDefault { get; init; }
}
