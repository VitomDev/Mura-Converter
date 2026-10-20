using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Mura_Converter;

/// <summary>
/// Thin wrapper around the bundled yt-dlp.exe (+ ffmpeg/ffprobe for merging, cutting and audio encoding).
/// </summary>
internal static class YtDlpService
{
    private const string ProgressTag = "[P]";
    private const string TitleTag = "[T]";
    private const string FileTag = "[F]";

    private static readonly string ToolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
    private static readonly string YtDlpPath = Path.Combine(ToolsDir, "yt-dlp.exe");

    // Sites whose links get auto-pasted from the clipboard. Any other URL can still be pasted by hand.
    private static readonly string[] KnownHosts =
    {
        "youtube.com", "youtu.be", "tiktok.com", "instagram.com", "x.com", "twitter.com", "twitch.tv",
        "soundcloud.com", "vimeo.com", "facebook.com", "fb.watch", "reddit.com", "dailymotion.com",
        "bilibili.com", "kick.com", "bandcamp.com",
    };

    public static bool LooksLikeUrl(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static bool IsKnownSiteUrl(string? text)
    {
        if (!LooksLikeUrl(text))
            return false;
        var host = new Uri(text!.Trim()).Host.ToLowerInvariant();
        return KnownHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));
    }

    public static bool HasPlaylist(string url) =>
        url.Contains("list=", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("/playlist", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("/sets/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Fetches title/thumbnail/duration for the preview. For links that carry a playlist, also its size.
    /// </summary>
    public static async Task<MediaInfo> GetInfoAsync(string url, CancellationToken cancellationToken)
    {
        var single = DumpJsonAsync(new[] { "--no-playlist" }, url, cancellationToken);
        var list = HasPlaylist(url)
            // Only the first entry: enough for title and size without paging through the whole list.
            ? DumpJsonAsync(new[] { "--yes-playlist", "--flat-playlist", "--playlist-end", "1" }, url, cancellationToken)
            : Task.FromResult<JsonElement?>(null);

        // A pure playlist link fails or returns the playlist itself in "single" mode; that's fine.
        JsonElement? video = null;
        try { video = await single; } catch (InvalidOperationException) when (HasPlaylist(url)) { }
        var playlist = await list;

        if (video == null && playlist == null)
            throw new InvalidOperationException("No se pudo leer el enlace.");

        string? playlistTitle = null;
        int? playlistCount = null;
        var isPlaylist = false;
        if (playlist is { } p && GetString(p, "_type") == "playlist")
        {
            isPlaylist = true;
            playlistTitle = GetString(p, "title");
            playlistCount = GetInt(p, "playlist_count");
        }

        var main = video ?? playlist!.Value;
        var duration = GetDouble(main, "duration");

        return new MediaInfo(
            Title: GetString(main, "title") ?? playlistTitle ?? "Sin título",
            Uploader: GetString(main, "uploader") ?? GetString(main, "channel"),
            Duration: duration is > 0 ? TimeSpan.FromSeconds(duration.Value) : null,
            ThumbnailUrl: GetString(main, "thumbnail") ?? FirstThumbnail(main),
            IsPlaylist: isPlaylist && playlistCount is null or > 1,
            PlaylistCount: playlistCount,
            PlaylistTitle: playlistTitle);
    }

    /// <summary>Runs one queued download and returns the last file written.</summary>
    public static async Task<string> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        EnsureTools();
        Directory.CreateDirectory(request.OutputFolder);

        var trimmed = request.TrimStart.HasValue || request.TrimEnd.HasValue;
        var name = trimmed ? "%(title).140B (recorte).%(ext)s" : "%(title).150B.%(ext)s";
        var output = request.WholePlaylist ? "%(playlist_title,playlist_id).80B/%(playlist_index)03d - " + name : name;

        var args = new List<string>
        {
            request.WholePlaylist ? "--yes-playlist" : "--no-playlist",
            "--ignore-errors",
            "--newline",
            "--no-simulate",
            "--progress",
            "--encoding", "utf-8",
            "--ffmpeg-location", ToolsDir,
            "--concurrent-fragments", "4",
            "--windows-filenames",
            "--no-mtime",
            "--paths", request.OutputFolder,
            "--paths", "temp:" + Path.Combine(Path.GetTempPath(), "MuraConverter"),
            "--output", output,
            "--progress-template",
            "download:" + ProgressTag + "%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.eta)s",
            "--print", "before_dl:" + TitleTag + "%(playlist_index|)s|%(n_entries|)s|%(title)s",
            "--print", "after_move:" + FileTag + "%(filepath)s",
            "--embed-metadata",
        };

        if (request.Kind == MediaKind.Audio)
        {
            var codec = request.AudioFormat.ToString().ToLowerInvariant();
            args.AddRange(new[] { "--format", "bestaudio/best", "--extract-audio", "--audio-format", codec });
            if (request.AudioFormat != AudioFormat.Wav)
            {
                args.AddRange(new[] { "--audio-quality", request.Quality + "K" });
                args.AddRange(new[] { "--embed-thumbnail", "--convert-thumbnails", "jpg" });
            }
        }
        else
        {
            // Highest resolution up to the cap; at equal resolution prefer H.264 + AAC so the
            // result plays everywhere, and merge into MP4 without re-encoding (fast).
            var sort = request.Quality > 0 ? $"res:{request.Quality},vcodec:h264,acodec:aac" : "vcodec:h264,acodec:aac";
            args.AddRange(new[]
            {
                "--format", "bv*+ba/b",
                "--format-sort", sort,
                "--merge-output-format", "mp4",
                "--remux-video", "mp4",
            });
        }

        if (trimmed)
        {
            // yt-dlp only fetches the requested section, so a 2-minute cut of a 3-hour stream is quick.
            var start = request.TrimStart ?? TimeSpan.Zero;
            var end = request.TrimEnd.HasValue ? request.TrimEnd.Value.TotalSeconds.ToString(CultureInfo.InvariantCulture) : "inf";
            args.AddRange(new[] { "--download-sections", $"*{start.TotalSeconds.ToString(CultureInfo.InvariantCulture)}-{end}" });
            if (request.Kind == MediaKind.Video)
                args.Add("--force-keyframes-at-cuts");
        }

        args.Add("--");
        args.Add(request.Url.Trim());

        string? lastPath = null;
        string? title = null;
        string? counter = null;
        var files = 0;
        var stream = 0;
        var lastPercent = 0d;
        var errors = new List<string>();
        var isAudio = request.Kind == MediaKind.Audio;

        void OnLine(string line)
        {
            if (line.StartsWith(ProgressTag, StringComparison.Ordinal))
            {
                var p = ParseProgress(line[ProgressTag.Length..]);
                // Video downloads fetch video then audio: a drop in percent means the second stream started.
                if (p.percent.HasValue && p.percent < lastPercent - 50)
                    stream++;
                if (p.percent.HasValue)
                    lastPercent = p.percent.Value;
                var stage = !isAudio && stream > 0 ? "Descargando audio" : "Descargando";
                progress?.Report(new DownloadProgress(counter + stage, p.percent, p.detail, title));
            }
            else if (line.StartsWith(TitleTag, StringComparison.Ordinal))
            {
                var parts = line[TitleTag.Length..].Split('|', 3);
                title = parts.Length == 3 ? parts[2] : line[TitleTag.Length..];
                counter = parts.Length == 3 && parts[0].Length > 0 && parts[1].Length > 0 ? $"{parts[0]}/{parts[1]} · " : null;
                stream = 0;
                lastPercent = 0;
                progress?.Report(new DownloadProgress(counter + "Descargando", 0, null, title));
            }
            else if (line.StartsWith(FileTag, StringComparison.Ordinal))
            {
                lastPath = line[FileTag.Length..].Trim();
                files++;
            }
            else if (line.StartsWith("[Merger]") || line.StartsWith("[ExtractAudio]") ||
                     line.StartsWith("[VideoRemuxer]") || line.StartsWith("[EmbedThumbnail]"))
            {
                var stage = isAudio ? $"Convirtiendo a {request.AudioFormat.ToString().ToUpperInvariant()}" : "Uniendo vídeo y audio";
                progress?.Report(new DownloadProgress(counter + stage, null, null, title));
            }
        }

        void OnError(string line)
        {
            if (line.StartsWith("ERROR:", StringComparison.Ordinal))
                errors.Add(line["ERROR:".Length..].Trim());
        }

        var exitCode = await RunAsync(args, OnLine, OnError, cancellationToken);

        // With --ignore-errors a playlist may finish with some failed entries; that still counts as done.
        if (lastPath == null)
        {
            var message = errors.Count > 0 ? errors[0] : $"yt-dlp terminó con código {exitCode}.";
            throw new InvalidOperationException(message);
        }

        return lastPath;
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

    /// <summary>Parses "90", "1:30" or "1:02:03" into a TimeSpan.</summary>
    public static bool TryParseTime(string? text, out TimeSpan? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        double seconds = 0;
        foreach (var part in text.Trim().Replace(',', '.').Split(':'))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n < 0)
                return false;
            seconds = seconds * 60 + n;
        }

        value = TimeSpan.FromSeconds(seconds);
        return true;
    }

    public static string FormatDuration(TimeSpan t) =>
        t.ToString(t.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    private static async Task<JsonElement?> DumpJsonAsync(string[] extra, string url, CancellationToken cancellationToken)
    {
        EnsureTools();
        var json = new StringBuilder();
        var errors = new StringBuilder();
        var args = new List<string> { "--dump-single-json", "--no-warnings", "--encoding", "utf-8" };
        args.AddRange(extra);
        args.Add("--");
        args.Add(url.Trim());

        var exit = await RunAsync(args, l => json.AppendLine(l), l => errors.AppendLine(l), cancellationToken);
        if (exit != 0 || json.Length == 0)
            throw new InvalidOperationException(errors.ToString().Replace("ERROR:", "").Trim());

        using var doc = JsonDocument.Parse(json.ToString());
        return doc.RootElement.Clone();
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? GetDouble(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static int? GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static string? FirstThumbnail(JsonElement e)
    {
        if (!e.TryGetProperty("thumbnails", out var thumbs) || thumbs.ValueKind != JsonValueKind.Array)
            return null;
        return thumbs.EnumerateArray().Select(t => GetString(t, "url")).LastOrDefault(u => u != null);
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
            detail.Append("  ·  ").Append(FormatDuration(TimeSpan.FromSeconds(eta.Value)));

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
