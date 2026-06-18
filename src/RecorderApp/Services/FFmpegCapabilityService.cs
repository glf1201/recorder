namespace RecorderApp.Services;

public sealed class FFmpegCapabilityService
{
    private readonly FileLogger _logger;
    private IReadOnlySet<string>? _cachedEncoders;
    private IReadOnlySet<string>? _cachedInputDevices;

    public FFmpegCapabilityService(FileLogger logger)
    {
        _logger = logger;
    }

    public async Task<string> SelectEncoderAsync(string ffmpegPath, string preferredCodec, CancellationToken cancellationToken)
    {
        var encoders = await GetAvailableEncodersAsync(ffmpegPath, cancellationToken);
        var wantedH265 = string.Equals(preferredCodec, "H265", StringComparison.OrdinalIgnoreCase);

        var ordered = wantedH265
            ? new[] { "libx265", "hevc_qsv", "hevc_nvenc", "hevc_amf" }
            : new[] { "libx264", "h264_qsv", "h264_nvenc", "h264_amf" };

        var encoder = ordered.FirstOrDefault(encoders.Contains) ?? ordered[^1];
        _logger.Info($"Selected encoder: {encoder}");
        return encoder;
    }

    public async Task<bool> SupportsInputDeviceAsync(string ffmpegPath, string inputDevice, CancellationToken cancellationToken)
    {
        var devices = await GetAvailableInputDevicesAsync(ffmpegPath, cancellationToken);
        return devices.Contains(inputDevice);
    }

    private async Task<IReadOnlySet<string>> GetAvailableEncodersAsync(string ffmpegPath, CancellationToken cancellationToken)
    {
        if (_cachedEncoders is not null)
        {
            return _cachedEncoders;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-hide_banner -encoders",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return _cachedEncoders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var combined = output + Environment.NewLine + error;

            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var reader = new StringReader(combined);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var trimmed = line.Trim();
                if (trimmed.Length < 8 || !char.IsLetter(trimmed[0]))
                {
                    continue;
                }

                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    result.Add(parts[1]);
                }
            }

            return _cachedEncoders = result;
        }
        catch (Exception exception)
        {
            _logger.Warn($"Failed to detect encoders, fallback to software encoder. {exception.Message}");
            return _cachedEncoders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<IReadOnlySet<string>> GetAvailableInputDevicesAsync(string ffmpegPath, CancellationToken cancellationToken)
    {
        if (_cachedInputDevices is not null)
        {
            return _cachedInputDevices;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-hide_banner -devices",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return _cachedInputDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var combined = output + Environment.NewLine + error;

            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var reader = new StringReader(combined);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var trimmed = line.Trim();
                if (trimmed.Length < 4 || trimmed.StartsWith("-") || trimmed.StartsWith("Devices", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0].Contains('D'))
                {
                    result.Add(parts[1]);
                }
            }

            return _cachedInputDevices = result;
        }
        catch (Exception exception)
        {
            _logger.Warn($"Failed to detect input devices. {exception.Message}");
            return _cachedInputDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
