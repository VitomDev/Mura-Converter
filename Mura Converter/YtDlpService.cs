using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Mura_Converter;

internal enum DownloadFormat
{
    Mp3,
    Mp4
}

internal sealed record DownloadProgress(string Stage, double? Percent, string? Detail);

/// <summary>
/// Thin wrapper around the bundled yt-dlp.exe (+ ffmpeg/ffprobe for merging and MP3 encoding).
/// </summary>
internal static class YtDlpService
{
    private const string ProgressTag = "[P]";
    private const string TitleTag = "[T]";
    private const string FileTag = "[F]";

    private static readonly string ToolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
    private static readonly string YtDlpPath = Path.Combine(ToolsDir, "yt-dlp.exe");

    public static bool LooksLikeUrl(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static bool IsYouTubeUrl(string? text) =>
        LooksLikeUrl(text) &&
        (text!.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("youtu.be", StringComparison.OrdinalIgnoreCase));

    /// <summary>Downloads the URL and returns the final file path.</summary>
    public static async Task<string> DownloadAsync(
        string url,
        DownloadFormat format,
        int videoHeight,
        int audioBitrate,
        string outputFolder,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        EnsureTools();
        Directory.CreateDirectory(outputFolder);

        var args = new List<string>
        {
            "--no-playlist",
            "--newline",
            "--no-simulate",
            "--progress",
            "--encoding", "utf-8",
            "--ffmpeg-location", ToolsDir,
            "--concurrent-fragments", "4",
            "--windows-filenames",
            "--no-mtime",
            "--paths", outputFolder,
            "--paths", "temp:" + Path.Combine(Path.GetTempPath(), "MuraConverter"),
            "--output", "%(title).150B.%(ext)s",
            "--progress-template",
            "download:" + ProgressTag + "%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.eta)s",
            "--print", "before_dl:" + TitleTag + "%(title)s",
            "--print", "after_move:" + FileTag + "%(filepath)s",
        };

        if (format == DownloadFormat.Mp3)
        {
            args.AddRange(new[]
            {
                "--format", "bestaudio/best",
                "--extract-audio",
                "--audio-format", "mp3",
                "--audio-quality", audioBitrate + "K",
                "--embed-metadata",
                "--embed-thumbnail",
                "--convert-thumbnails", "jpg",
            });
        }
        else
        {
            // Highest resolution up to the cap; at equal resolution prefer H.264 + AAC so the
            // result plays everywhere, and merge into MP4 without re-encoding (fast).
            var sort = videoHeight > 0 ? $"res:{videoHeight},vcodec:h264,acodec:aac" : "vcodec:h264,acodec:aac";
            args.AddRange(new[]
            {
                "--format", "bv*+ba/b",
                "--format-sort", sort,
                "--merge-output-format", "mp4",
                "--remux-video", "mp4",
                "--embed-metadata",
            });
        }

        args.Add("--");
        args.Add(url.Trim());

        string? finalPath = null;
        string? title = null;
        var stream = 0;
        var lastPercent = 0d;
        var errors = new StringBuilder();

        void OnLine(string line)
        {
            if (line.StartsWith(ProgressTag, StringComparison.Ordinal))
            {
                var p = ParseProgress(line[ProgressTag.Length..]);
                // MP4 downloads video then audio: a drop in percent means the second stream started.
                if (p.percent.HasValue && p.percent < lastPercent - 50)
                    stream++;
                if (p.percent.HasValue)
                    lastPercent = p.percent.Value;
                var stage = format == DownloadFormat.Mp4 && stream > 0 ? "Descargando audio" : "Descargando";
                progress?.Report(new DownloadProgress(stage, p.percent, p.detail));
            }
            else if (line.StartsWith(TitleTag, StringComparison.Ordinal))
            {
                title = line[TitleTag.Length..];
                progress?.Report(new DownloadProgress("Descargando", 0, title));
            }
            else if (line.StartsWith(FileTag, StringComparison.Ordinal))
            {
                finalPath = line[FileTag.Length..].Trim();
            }
            else if (line.StartsWith("[Merger]") || line.StartsWith("[ExtractAudio]") ||
                     line.StartsWith("[VideoRemuxer]") || line.StartsWith("[EmbedThumbnail]"))
            {
                progress?.Report(new DownloadProgress(format == DownloadFormat.Mp3 ? "Convirtiendo a MP3" : "Uniendo vídeo y audio", null, title));
            }
        }

        void OnError(string line)
        {
            if (line.StartsWith("ERROR:", StringComparison.Ordinal))
                errors.AppendLine(line["ERROR:".Length..].Trim());
        }

        var exitCode = await RunAsync(args, OnLine, OnError, cancellationToken);

        if (exitCode != 0 || finalPath == null)
        {
            var message = errors.Length > 0 ? errors.ToString().Trim() : $"yt-dlp terminó con código {exitCode}.";
            throw new InvalidOperationException(message);
        }

        return finalPath;
    }

    /// <summary>
    /// Keeps yt-dlp current: YouTube changes often and an outdated yt-dlp is the #1 cause of failures.
    /// </summary>
    public static async Task<bool> TryUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureTools();
            var exit = await RunAsync(new[] { "--update" }, _ => { }, _ => { }, cancellationToken);
            return exit == 0;
        }
        catch
        {
            return false;
        }
    }

    private static (double? percent, string? detail) ParseProgress(string payload)
    {
        var parts = payload.Split('|');
        if (parts.Length < 5)
            return (null, null);

        var downloaded = ParseDouble(parts[0]);
        var total = ParseDouble(parts[1]) ?? ParseDouble(parts[2]);
        var speed = ParseDouble(parts[3]);
        var eta = ParseDouble(parts[4]);

        double? percent = downloaded.HasValue && total is > 0 ? Math.Min(100, downloaded.Value * 100 / total.Value) : null;

        var detail = new StringBuilder();
        if (total is > 0)
            detail.Append(FormatBytes(downloaded ?? 0)).Append(" / ").Append(FormatBytes(total.Value));
        if (speed is > 0)
            detail.Append("  ·  ").Append(FormatBytes(speed.Value)).Append("/s");
        if (eta is > 0)
            detail.Append("  ·  ").Append(TimeSpan.FromSeconds(eta.Value).ToString(eta >= 3600 ? @"h\:mm\:ss" : @"m\:ss"));

        return (percent, detail.ToString());
    }

    private static double? ParseDouble(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string FormatBytes(double bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        var i = 0;
        while (bytes >= 1024 && i < units.Length - 1)
        {
            bytes /= 1024;
            i++;
        }
        return bytes.ToString(i == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[i];
    }

    private static void EnsureTools()
    {
        if (!File.Exists(YtDlpPath))
            throw new FileNotFoundException("No se encontró yt-dlp.exe. Reinstala Mura Converter.", YtDlpPath);
    }

    private static async Task<int> RunAsync(
        IEnumerable<string> arguments,
        Action<string> onOutput,
        Action<string> onError,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = YtDlpPath,
            WorkingDirectory = ToolsDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) onOutput(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) onError(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }

        // Flush the async readers.
        process.WaitForExit();
        return process.ExitCode;
    }
}
