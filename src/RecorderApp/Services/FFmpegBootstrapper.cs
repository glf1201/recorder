using RecorderApp.Models;
using System.IO.Compression;
using System.Net.Http;

namespace RecorderApp.Services;

public sealed class FFmpegBootstrapper
{
    private const string DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    private readonly FFmpegLocator _locator;
    private readonly FileLogger _logger;

    public FFmpegBootstrapper(FFmpegLocator locator, FileLogger logger)
    {
        _locator = locator;
        _logger = logger;
    }

    public async Task<FfmpegBootstrapResult> EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        var existing = _locator.Locate();
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return new FfmpegBootstrapResult
            {
                IsAvailable = true,
                ExecutablePath = existing,
                Message = $"FFmpeg 已就绪：{existing}",
            };
        }

        try
        {
            AppPaths.EnsureDirectories(AppPaths.DefaultRecordDirectory);
            Directory.CreateDirectory(AppPaths.ToolsDirectory);
            Directory.CreateDirectory(AppPaths.FfmpegBinDirectory);
            Directory.CreateDirectory(AppPaths.DownloadCacheDirectory);

            if (TryCopyFromLocalBundle(out var copiedPath))
            {
                return new FfmpegBootstrapResult
                {
                    IsAvailable = true,
                    ExecutablePath = copiedPath,
                    Message = $"FFmpeg 已从本地目录接入：{copiedPath}",
                };
            }

            var archivePath = Path.Combine(AppPaths.DownloadCacheDirectory, "ffmpeg-release-essentials.zip");
            await DownloadArchiveAsync(archivePath, cancellationToken);
            await ExtractArchiveAsync(archivePath, cancellationToken);

            var resolved = _locator.Locate();
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return new FfmpegBootstrapResult
                {
                    IsAvailable = true,
                    ExecutablePath = resolved,
                    Message = $"FFmpeg 已自动部署：{resolved}",
                };
            }

            return new FfmpegBootstrapResult
            {
                IsAvailable = false,
                Message = "FFmpeg 下载完成，但未找到可执行文件。",
            };
        }
        catch (Exception exception)
        {
            _logger.Warn($"FFmpeg bootstrap failed. {exception.Message}");
            return new FfmpegBootstrapResult
            {
                IsAvailable = false,
                Message = "FFmpeg 自动部署失败，请手动放置 ffmpeg.exe 到 Tools/ffmpeg/bin/。",
            };
        }
    }

    private bool TryCopyFromLocalBundle(out string executablePath)
    {
        var localBinCandidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "ffmpeg", "bin"),
            @"C:\record\ffmpeg\bin",
        };

        foreach (var candidate in localBinCandidates)
        {
            var ffmpegPath = Path.Combine(candidate, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
            {
                continue;
            }

            Directory.CreateDirectory(AppPaths.FfmpegBinDirectory);
            foreach (var file in Directory.EnumerateFiles(candidate))
            {
                var destination = Path.Combine(AppPaths.FfmpegBinDirectory, Path.GetFileName(file));
                File.Copy(file, destination, true);
            }

            executablePath = Path.Combine(AppPaths.FfmpegBinDirectory, "ffmpeg.exe");
            _logger.Info($"FFmpeg copied from local bundle: {candidate}");
            return true;
        }

        executablePath = string.Empty;
        return false;
    }

    private async Task DownloadArchiveAsync(string archivePath, CancellationToken cancellationToken)
    {
        if (File.Exists(archivePath) && new FileInfo(archivePath).Length > 0)
        {
            _logger.Info($"Using cached FFmpeg archive: {archivePath}");
            return;
        }

        _logger.Info($"Downloading FFmpeg from {DownloadUrl}");
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(archivePath);
        await source.CopyToAsync(target, cancellationToken);
    }

    private Task ExtractArchiveAsync(string archivePath, CancellationToken cancellationToken)
    {
        var extractionRoot = Path.Combine(AppPaths.DownloadCacheDirectory, "ffmpeg-extracted");
        if (Directory.Exists(extractionRoot))
        {
            Directory.Delete(extractionRoot, true);
        }

        ZipFile.ExtractToDirectory(archivePath, extractionRoot);
        var executable = Directory.EnumerateFiles(extractionRoot, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new FileNotFoundException("ffmpeg.exe not found in extracted archive.");
        }

        var sourceBinDirectory = Path.GetDirectoryName(executable)!;
        foreach (var file in Directory.EnumerateFiles(sourceBinDirectory))
        {
            var destination = Path.Combine(AppPaths.FfmpegBinDirectory, Path.GetFileName(file));
            File.Copy(file, destination, true);
        }

        _logger.Info($"FFmpeg extracted to {AppPaths.FfmpegBinDirectory}");
        return Task.CompletedTask;
    }
}
