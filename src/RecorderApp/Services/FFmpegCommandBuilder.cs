using RecorderApp.Models;
using System.Windows.Forms;

namespace RecorderApp.Services;

public sealed class FFmpegCommandBuilder
{
    public string BuildArguments(RecorderSettings settings, string outputPath, string encoder, int durationSeconds, AudioLoopbackCaptureSession? audioSession)
    {
        var extension = GetExtension(settings.Container);
        var includeAudio = audioSession is not null;
        var arguments = new List<string>
        {
            "-hide_banner",
            "-y",
            "-loglevel info",
            $"-f gdigrab -framerate {settings.FrameRate}"
        };

        var targetScreen = ResolveScreen(settings.DisplayTarget);
        if (targetScreen is null)
        {
            arguments.Add("-i desktop");
        }
        else
        {
            arguments.Add($"-offset_x {targetScreen.Bounds.X}");
            arguments.Add($"-offset_y {targetScreen.Bounds.Y}");
            arguments.Add($"-video_size {targetScreen.Bounds.Width}x{targetScreen.Bounds.Height}");
            arguments.Add("-i desktop");
        }

        if (includeAudio && audioSession is not null)
        {
            arguments.Add("-thread_queue_size 512");
            arguments.Add($"-f {audioSession.FfmpegSampleFormat}");
            arguments.Add($"-ar {audioSession.SampleRate}");
            arguments.Add($"-ac {audioSession.Channels}");
            arguments.Add($"-i {Quote(audioSession.PipePath)}");
        }

        arguments.Add($"-t {durationSeconds}");
        arguments.Add("-vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\"");
        if (includeAudio)
        {
            arguments.Add("-map 0:v:0");
            arguments.Add("-map 1:a:0");
        }

        arguments.Add($"-c:v {encoder}");
        arguments.AddRange(BuildEncoderOptions(settings.Quality, encoder));
        arguments.Add("-pix_fmt yuv420p");
        if (includeAudio)
        {
            arguments.Add("-c:a aac");
            arguments.Add("-b:a 128k");
        }
        if (string.Equals(extension, "mp4", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add("-movflags +faststart");
        }

        arguments.Add(Quote(outputPath));

        return string.Join(' ', arguments);
    }

    public string GetExtension(string container)
    {
        return string.Equals(container, "mp4", StringComparison.OrdinalIgnoreCase) ? "mp4" : "mkv";
    }

    private static IEnumerable<string> BuildEncoderOptions(int quality, string encoder)
    {
        if (encoder.Contains("nvenc", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "-preset p5", $"-cq {quality}" };
        }

        if (encoder.Contains("qsv", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "-preset medium", $"-global_quality {quality}" };
        }

        if (encoder.Contains("amf", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "-quality quality", $"-qp_i {quality}", $"-qp_p {quality}" };
        }

        return new[] { "-preset veryfast", $"-crf {quality}" };
    }

    private static Screen? ResolveScreen(string displayTarget)
    {
        if (string.IsNullOrWhiteSpace(displayTarget) || string.Equals(displayTarget, "AllDisplays", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Screen.AllScreens.FirstOrDefault(screen => string.Equals(screen.DeviceName, displayTarget, StringComparison.OrdinalIgnoreCase));
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"") + '"';
}
