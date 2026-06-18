namespace RecorderApp.Models;

public sealed class RecordingSessionState
{
    public bool CleanShutdown { get; set; }

    public string? CurrentOutputPath { get; set; }

    public string? CurrentAudioPath { get; set; }

    public DateTime LastHeartbeatUtc { get; set; }

    public DateTime LastUpdatedUtc { get; set; }
}
