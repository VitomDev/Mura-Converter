using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace Mura_Converter;

internal enum DownloadFormat
{
    Mp3,
    Mp4
}

internal sealed record DownloadResult(string FilePath, string VideoTitle);
internal sealed record VideoQualityOption(string Key, string Label, bool IsMuxed, int SortOrder);

internal static class YouTubeDownloadService
{
    public static VideoQualityOption AutoQualityOption { get; } =
        new("auto", "Auto (best available)", true, int.MaxValue);

    public static VideoQualityOption LoadingQualityOption { get; } =
        new("loading", "Loading qualities...", true, int.MinValue);

    public static bool IsValidYouTubeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host.ToLowerInvariant();
        return host.Contains("youtube.com") || host.Contains("youtu.be");
    }

    public static async Task<IReadOnlyList<VideoQualityOption>> GetMp4QualityOptionsAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidYouTubeUrl(url))
            throw new ArgumentException("Please provide a valid YouTube URL.", nameof(url));

        var youtube = new YoutubeClient();
        var video = await youtube.Videos.GetAsync(url, cancellationToken);
        var manifest = await youtube.Videos.Streams.GetManifestAsync(video.Id, cancellationToken);

        var options = manifest.GetMuxedStreams()
            .Select(ToQualityOption)
            .Concat(manifest.GetVideoOnlyStreams().Select(ToQualityOption))
            .GroupBy(o => o.Key)
            .Select(g => g.First())
            .OrderByDescending(o => o.SortOrder)
            .ThenByDescending(o => o.IsMuxed)
            .ThenBy(o => o.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        options.Insert(0, AutoQualityOption);
        return options;
    }

    public static async Task<DownloadResult> DownloadAsync(
        string url,
        DownloadFormat format,
        string outputFolder,
        string? preferredMp4QualityKey = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidYouTubeUrl(url))
            throw new ArgumentException("Please provide a valid YouTube URL.", nameof(url));

        Directory.CreateDirectory(outputFolder);

        var youtube = new YoutubeClient();
        var video = await youtube.Videos.GetAsync(url, cancellationToken);
        var manifest = await youtube.Videos.Streams.GetManifestAsync(video.Id, cancellationToken);

        var safeTitle = BuildSafeFileName(video.Title);
        var baseName = $"{safeTitle} ({DateTime.Now:yyyy-MM-dd})";
        var ffmpegPath = ResolveFfmpegPath();

        return format switch
        {
            DownloadFormat.Mp3 => await DownloadMp3Async(
                youtube, manifest, baseName, outputFolder, video.Title, ffmpegPath, cancellationToken),
            DownloadFormat.Mp4 => await DownloadMp4Async(
                youtube, manifest, baseName, outputFolder, video.Title, ffmpegPath, preferredMp4QualityKey, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    private static async Task<DownloadResult> DownloadMp3Async(
        YoutubeClient youtube,
        StreamManifest manifest,
        string baseName,
        string outputFolder,
        string videoTitle,
        string ffmpegPath,
        CancellationToken cancellationToken)
    {
        var audioStream = SelectBestAudioStream(manifest)
            ?? throw new InvalidOperationException("No audio stream available for this video.");

        var inputPath = Path.Combine(Path.GetTempPath(), $"mura-audio-{Guid.NewGuid():N}.{audioStream.Container.Name}");
        var outputPath = GetUniqueFilePath(outputFolder, baseName, ".mp3");

        try
        {
            await youtube.Videos.Streams.DownloadAsync(audioStream, inputPath, null, cancellationToken);

            await RunFfmpegAsync(
                ffmpegPath,
                new[]
                {
                    "-y",
                    "-i", inputPath,
                    "-vn",
                    "-codec:a", "libmp3lame",
                    "-q:a", "2",
                    outputPath
                },
                cancellationToken);

            return new DownloadResult(outputPath, videoTitle);
        }
        finally
        {
            TryDeleteFile(inputPath);
        }
    }

    private static async Task<DownloadResult> DownloadMp4Async(
        YoutubeClient youtube,
        StreamManifest manifest,
        string baseName,
        string outputFolder,
        string videoTitle,
        string ffmpegPath,
        string? preferredMp4QualityKey,
        CancellationToken cancellationToken)
    {
        var outputPath = GetUniqueFilePath(outputFolder, baseName, ".mp4");

        if (TryResolvePreferredStream(manifest, preferredMp4QualityKey, out var preferredMuxed, out var preferredVideo))
        {
            if (preferredMuxed != null)
            {
                await DownloadFromMuxedStreamAsync(youtube, preferredMuxed, ffmpegPath, outputPath, cancellationToken);
                return new DownloadResult(outputPath, videoTitle);
            }

            if (preferredVideo != null)
            {
                var preferredAudio = SelectBestAudioStream(manifest)
                    ?? throw new InvalidOperationException("No audio stream available to merge with selected quality.");

                await DownloadFromSeparateStreamsAsync(
                    youtube, preferredVideo, preferredAudio, ffmpegPath, outputPath, cancellationToken);
                return new DownloadResult(outputPath, videoTitle);
            }
        }

        var muxedMp4 = SelectBestMuxedStream(manifest, onlyMp4Container: true);
        if (muxedMp4 != null)
        {
            await youtube.Videos.Streams.DownloadAsync(muxedMp4, outputPath, null, cancellationToken);
            return new DownloadResult(outputPath, videoTitle);
        }

        var videoStream = SelectBestVideoOnlyStream(manifest);
        var audioStream = SelectBestAudioStream(manifest);
        if (videoStream != null && audioStream != null)
        {
            await DownloadFromSeparateStreamsAsync(
                youtube, videoStream, audioStream, ffmpegPath, outputPath, cancellationToken);
            return new DownloadResult(outputPath, videoTitle);
        }

        var muxedAny = SelectBestMuxedStream(manifest, onlyMp4Container: false)
            ?? throw new InvalidOperationException("No compatible video stream found.");

        await DownloadFromMuxedStreamAsync(youtube, muxedAny, ffmpegPath, outputPath, cancellationToken);
        return new DownloadResult(outputPath, videoTitle);
    }

    private static bool TryResolvePreferredStream(
        StreamManifest manifest,
        string? preferredMp4QualityKey,
        out MuxedStreamInfo? muxedStream,
        out VideoOnlyStreamInfo? videoStream)
    {
        muxedStream = null;
        videoStream = null;

        if (string.IsNullOrWhiteSpace(preferredMp4QualityKey) ||
            string.Equals(preferredMp4QualityKey, AutoQualityOption.Key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = preferredMp4QualityKey.Split('|', 5, StringSplitOptions.None);
        if (parts.Length != 5)
            return false;

        var type = parts[0];
        var qualityLabel = parts[1];
        var container = parts[2];
        var codec = parts[3];
        var bitrate = parts[4];

        if (string.Equals(type, "m", StringComparison.OrdinalIgnoreCase))
        {
            muxedStream = manifest.GetMuxedStreams().FirstOrDefault(s =>
                MatchesStream(s.VideoQuality.Label, s.Container.Name, s.VideoCodec, s.Bitrate.ToString(), qualityLabel, container, codec, bitrate));
        }
        else if (string.Equals(type, "v", StringComparison.OrdinalIgnoreCase))
        {
            videoStream = manifest.GetVideoOnlyStreams().FirstOrDefault(s =>
                MatchesStream(s.VideoQuality.Label, s.Container.Name, s.VideoCodec, s.Bitrate.ToString(), qualityLabel, container, codec, bitrate));
        }

        return muxedStream != null || videoStream != null;
    }

    private static async Task DownloadFromMuxedStreamAsync(
        YoutubeClient youtube,
        MuxedStreamInfo muxedStream,
        string ffmpegPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (muxedStream.Container == Container.Mp4)
        {
            await youtube.Videos.Streams.DownloadAsync(muxedStream, outputPath, null, cancellationToken);
            return;
        }

        var tempMuxedPath = Path.Combine(Path.GetTempPath(), $"mura-muxed-{Guid.NewGuid():N}.{muxedStream.Container.Name}");

        try
        {
            await youtube.Videos.Streams.DownloadAsync(muxedStream, tempMuxedPath, null, cancellationToken);
            await RemuxOrTranscodeToMp4Async(ffmpegPath, tempMuxedPath, outputPath, cancellationToken);
        }
        finally
        {
            TryDeleteFile(tempMuxedPath);
        }
    }

    private static async Task DownloadFromSeparateStreamsAsync(
        YoutubeClient youtube,
        VideoOnlyStreamInfo videoStream,
        AudioOnlyStreamInfo audioStream,
        string ffmpegPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var tempVideoPath = Path.Combine(Path.GetTempPath(), $"mura-video-{Guid.NewGuid():N}.{videoStream.Container.Name}");
        var tempAudioPath = Path.Combine(Path.GetTempPath(), $"mura-audio-{Guid.NewGuid():N}.{audioStream.Container.Name}");

        try
        {
            await youtube.Videos.Streams.DownloadAsync(videoStream, tempVideoPath, null, cancellationToken);
            await youtube.Videos.Streams.DownloadAsync(audioStream, tempAudioPath, null, cancellationToken);
            await MergeStreamsToMp4Async(ffmpegPath, tempVideoPath, tempAudioPath, outputPath, cancellationToken);
        }
        finally
        {
            TryDeleteFile(tempVideoPath);
            TryDeleteFile(tempAudioPath);
        }
    }

    private static VideoQualityOption ToQualityOption(MuxedStreamInfo stream)
    {
        var bitrate = stream.Bitrate.ToString();
        var key = BuildStreamKey("m", stream.VideoQuality.Label, stream.Container.Name, stream.VideoCodec, bitrate);
        var label = $"{stream.VideoQuality.Label} - {stream.Container.Name.ToUpperInvariant()} (video+audio)";
        return new VideoQualityOption(key, label, true, BuildSortScore(stream.VideoQuality.Label, bitrate));
    }

    private static VideoQualityOption ToQualityOption(VideoOnlyStreamInfo stream)
    {
        var bitrate = stream.Bitrate.ToString();
        var key = BuildStreamKey("v", stream.VideoQuality.Label, stream.Container.Name, stream.VideoCodec, bitrate);
        var label = $"{stream.VideoQuality.Label} - {stream.Container.Name.ToUpperInvariant()} (video only)";
        return new VideoQualityOption(key, label, false, BuildSortScore(stream.VideoQuality.Label, bitrate));
    }

    private static string BuildStreamKey(string type, string qualityLabel, string container, string codec, string bitrate) =>
        string.Join('|', type, qualityLabel, container, codec, bitrate);

    private static bool MatchesStream(
        string qualityLabel,
        string container,
        string codec,
        string bitrate,
        string expectedQualityLabel,
        string expectedContainer,
        string expectedCodec,
        string expectedBitrate) =>
        string.Equals(qualityLabel, expectedQualityLabel, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(container, expectedContainer, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(codec, expectedCodec, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(bitrate, expectedBitrate, StringComparison.OrdinalIgnoreCase);

    private static MuxedStreamInfo? SelectBestMuxedStream(StreamManifest manifest, bool onlyMp4Container)
    {
        var streams = manifest.GetMuxedStreams();
        if (onlyMp4Container)
            streams = streams.Where(s => s.Container == Container.Mp4);

        return streams
            .OrderByDescending(s => BuildSortScore(s.VideoQuality.Label, s.Bitrate.ToString()))
            .FirstOrDefault();
    }

    private static VideoOnlyStreamInfo? SelectBestVideoOnlyStream(StreamManifest manifest) =>
        manifest.GetVideoOnlyStreams()
            .OrderByDescending(s => BuildSortScore(s.VideoQuality.Label, s.Bitrate.ToString()))
            .FirstOrDefault();

    private static AudioOnlyStreamInfo? SelectBestAudioStream(StreamManifest manifest) =>
        manifest.GetAudioOnlyStreams()
            .OrderByDescending(s => ParseBitrateScore(s.Bitrate.ToString()))
            .FirstOrDefault();

    private static int BuildSortScore(string qualityLabel, string bitrateText)
    {
        var quality = ParseQualityScore(qualityLabel);
        var bitrate = ParseBitrateScore(bitrateText);
        return (quality * 100_000) + bitrate;
    }

    private static int ParseQualityScore(string qualityLabel)
    {
        if (string.IsNullOrWhiteSpace(qualityLabel))
            return 0;

        var value = 0;
        foreach (var ch in qualityLabel)
        {
            if (char.IsDigit(ch))
            {
                value = (value * 10) + (ch - '0');
                continue;
            }

            if (value > 0)
                break;
        }

        return value;
    }

    private static int ParseBitrateScore(string bitrateText)
    {
        if (string.IsNullOrWhiteSpace(bitrateText))
            return 0;

        var digits = new string(bitrateText.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var score) ? score : 0;
    }

    private static async Task MergeStreamsToMp4Async(
        string ffmpegPath,
        string videoPath,
        string audioPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await RunFfmpegAsync(
                ffmpegPath,
                new[]
                {
                    "-y",
                    "-i", videoPath,
                    "-i", audioPath,
                    "-map", "0:v:0",
                    "-map", "1:a:0",
                    "-c:v", "copy",
                    "-c:a", "copy",
                    outputPath
                },
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            await RunFfmpegAsync(
                ffmpegPath,
                new[]
                {
                    "-y",
                    "-i", videoPath,
                    "-i", audioPath,
                    "-map", "0:v:0",
                    "-map", "1:a:0",
                    "-c:v", "libx264",
                    "-preset", "veryfast",
                    "-crf", "23",
                    "-c:a", "aac",
                    "-b:a", "192k",
                    outputPath
                },
                cancellationToken);
        }
    }

    private static async Task RemuxOrTranscodeToMp4Async(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await RunFfmpegAsync(
                ffmpegPath,
                new[]
                {
                    "-y",
                    "-i", inputPath,
                    "-c:v", "copy",
                    "-c:a", "copy",
                    outputPath
                },
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            await RunFfmpegAsync(
                ffmpegPath,
                new[]
                {
                    "-y",
                    "-i", inputPath,
                    "-c:v", "libx264",
                    "-preset", "veryfast",
                    "-crf", "23",
                    "-c:a", "aac",
                    "-b:a", "192k",
                    outputPath
                },
                cancellationToken);
        }
    }

    private static async Task RunFfmpegAsync(string ffmpegPath, string[] arguments, CancellationToken cancellationToken)
    {
        if (!File.Exists(ffmpegPath))
            throw new FileNotFoundException("ffmpeg.exe was not found. Please make sure it is bundled with the app.", ffmpegPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var errorTask = process.StandardError.ReadToEndAsync();
        var outputTask = process.StandardOutput.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        var error = await errorTask;
        _ = await outputTask;

        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(error)
                ? "Unknown FFmpeg error."
                : error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Unknown FFmpeg error.";
            throw new InvalidOperationException($"FFmpeg failed: {message}");
        }
    }

    private static string ResolveFfmpegPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe"),
            Path.Combine(Environment.CurrentDirectory, "ffmpeg", "ffmpeg.exe")
        };

        var path = candidates.FirstOrDefault(File.Exists);
        if (path != null)
            return path;

        throw new FileNotFoundException("ffmpeg.exe not found in application folder.");
    }

    private static string BuildSafeFileName(string title)
    {
        var safe = string.Concat(title.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "video" : safe;
    }

    private static string GetUniqueFilePath(string folder, string baseName, string extension)
    {
        var filePath = Path.Combine(folder, $"{baseName}{extension}");
        var counter = 2;

        while (File.Exists(filePath))
        {
            filePath = Path.Combine(folder, $"{baseName} ({counter}){extension}");
            counter++;
        }

        return filePath;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignore cleanup errors.
        }
    }
}
