using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clipper98;

/// <summary>Semua urusan yt-dlp: membaca info video dan mengunduh satu bagian video.</summary>
public sealed class YtDlpService
{
    // Tuas percobaan: ubah ke false untuk membandingkan kecepatan unduh tanpa re-encode di titik potong.
    // Catatan: tanpa ini, awal klip bisa mundur sampai satu keyframe dari titik yang diminta.
    private static readonly bool ForceKeyframesAtCuts = false;

    private const string FormatSelector =
        "bestvideo[height<=1080][fps<=30][vcodec^=avc1][protocol^=m3u8]+bestaudio[protocol^=m3u8]/bestvideo[height<=1080][fps<=30][protocol^=m3u8]+bestaudio[protocol^=m3u8]/bestvideo[height<=1080][fps<=30][vcodec^=avc1][ext=mp4]+bestaudio[ext=m4a]/best[height<=1080][fps<=30]";

    private static readonly Regex NumberPattern = new(@"\d+(?:[.,]\d+)?", RegexOptions.Compiled);
    private static readonly Regex FfmpegTime = new(@"time=(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex FfmpegSpeed = new(@"speed=\s*([\d.]+)x", RegexOptions.Compiled);

    public async Task<(string Title, double Duration)> GetInfoAsync(string url, CancellationToken ct)
    {
        var ytdlp = ProcessRunner.RequireTool("yt-dlp");
        var result = await ProcessRunner.RunAsync(
            ytdlp,
            ["--no-playlist", "--js-runtimes", "node", "--skip-download", "--dump-single-json", "--no-warnings", "--no-progress", url],
            cancellationToken: ct);
        if (result.ExitCode != 0) throw new InvalidOperationException("Could not read the video. " + result.LastLine);

        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        var title = root.TryGetProperty("title", out var titleNode) && titleNode.ValueKind == JsonValueKind.String
            ? titleNode.GetString() ?? "Video YouTube"
            : "Video YouTube";
        var duration = root.TryGetProperty("duration", out var durationNode) && durationNode.ValueKind == JsonValueKind.Number
            ? durationNode.GetDouble()
            : 0;
        return (title, duration);
    }

    /// <summary>Downloads one matching YouTube subtitle track without downloading the video.</summary>
    public async Task<SubtitleDownload?> DownloadSubtitleAsync(
        string url, string language, string workDir, CancellationToken ct)
    {
        var ytdlp = ProcessRunner.RequireTool("yt-dlp");
        var info = await ProcessRunner.RunAsync(
            ytdlp,
            ["--no-playlist", "--js-runtimes", "node", "--skip-download", "--dump-single-json", "--no-warnings", "--no-progress", url],
            workDir,
            cancellationToken: ct);
        if (info.ExitCode != 0) throw new InvalidOperationException("Could not read the YouTube subtitle list. " + info.LastLine);

        using var document = JsonDocument.Parse(info.Output);
        var choice = ChooseSubtitle(document.RootElement, language);
        if (choice is null) return null;

        var outputTemplate = Path.Combine(workDir, "youtube-subtitles.%(ext)s");
        var args = new List<string>
        {
            "--no-playlist", "--js-runtimes", "node", "--skip-download", "--no-warnings", "--no-progress",
            choice.IsAutomatic ? "--write-auto-subs" : "--write-subs",
            "--sub-langs", choice.Language,
            "--sub-format", "vtt/srt/best",
            "--convert-subs", "vtt",
            "-o", outputTemplate,
            url
        };
        var result = await ProcessRunner.RunAsync(ytdlp, args, workDir, cancellationToken: ct);
        if (result.ExitCode != 0) throw new InvalidOperationException("Could not download YouTube subtitles. " + result.LastLine);

        var subtitlePath = Directory.EnumerateFiles(workDir, "youtube-subtitles*.vtt").FirstOrDefault()
            ?? Directory.EnumerateFiles(workDir, "youtube-subtitles*.srt").FirstOrDefault();
        return subtitlePath is null ? null : new SubtitleDownload(subtitlePath, choice.Language, choice.IsAutomatic);
    }

    private static SubtitleChoice? ChooseSubtitle(JsonElement root, string requestedLanguage)
    {
        var manual = GetSubtitleLanguages(root, "subtitles", isAutomatic: false);
        var automatic = GetSubtitleLanguages(root, "automatic_captions", isAutomatic: true);
        var requested = requestedLanguage.Trim();

        if (requested.Equals("id", StringComparison.OrdinalIgnoreCase)
            || requested.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            return FindLanguage(manual, requested) ?? FindLanguage(automatic, requested);
        }

        var sourceLanguage = ReadString(root, "language") ?? ReadString(root, "language_code");
        if (!string.IsNullOrWhiteSpace(sourceLanguage))
        {
            var sourceTrack = FindLanguage(manual, sourceLanguage) ?? FindLanguage(automatic, sourceLanguage);
            if (sourceTrack is not null) return sourceTrack;
        }

        return manual.FirstOrDefault(IsOriginalTrack)
            ?? automatic.FirstOrDefault(IsOriginalTrack)
            ?? (manual.Concat(automatic).Select(track => track.Language).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
                ? manual.FirstOrDefault() ?? automatic.FirstOrDefault()
                : null);
    }

    private static List<SubtitleChoice> GetSubtitleLanguages(JsonElement root, string property, bool isAutomatic)
    {
        if (!root.TryGetProperty(property, out var languages) || languages.ValueKind != JsonValueKind.Object) return [];
        return languages.EnumerateObject()
            .Where(item => !item.Name.Contains("live_chat", StringComparison.OrdinalIgnoreCase))
            .Select(item => new SubtitleChoice(item.Name, isAutomatic))
            .OrderBy(track => IsOriginalTrack(track) ? 0 : 1)
            .ToList();
    }

    private static SubtitleChoice? FindLanguage(IEnumerable<SubtitleChoice> choices, string requested)
    {
        var requestedBase = BaseLanguage(requested);
        return choices
            .Where(track => BaseLanguage(track.Language).Equals(requestedBase, StringComparison.OrdinalIgnoreCase))
            .OrderBy(track => IsOriginalTrack(track) ? 0 : 1)
            .FirstOrDefault();
    }

    private static string BaseLanguage(string language) =>
        language.Split('-', '_', '.')[0];

    private static bool IsOriginalTrack(SubtitleChoice track) =>
        track.Language.EndsWith("-orig", StringComparison.OrdinalIgnoreCase)
        || track.Language.EndsWith("_orig", StringComparison.OrdinalIgnoreCase)
        || track.Language.EndsWith(".orig", StringComparison.OrdinalIgnoreCase);

    private static string? ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Mengunduh rentang [start, end] detik. Mengembalikan path file mp4 hasil unduhan.</summary>
    public async Task<string> DownloadSectionAsync(
        string url, double start, double end, string destination, string workDir,
        Action<JobProgress> report, CancellationToken ct)
    {
        var ytdlp = ProcessRunner.RequireTool("yt-dlp");
        var sectionLength = Math.Max(1, end - start);

        var args = new List<string>
        {
            "--no-playlist", "--js-runtimes", "node", "-N", "4", "--newline",
            "--progress-template",
            "download:CLIPPER|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s|%(progress._downloaded_bytes_str)s",
            "-f", FormatSelector,
            "--download-sections", $"*{Seconds(start)}-{Seconds(end)}"
        };
        if (ForceKeyframesAtCuts) args.Add("--force-keyframes-at-cuts");
        args.AddRange(["--merge-output-format", "mp4", "-o", destination, url]);

        var result = await ProcessRunner.RunAsync(ytdlp, args, workDir, line => HandleLine(line, sectionLength, report), ct);
        if (result.ExitCode != 0) throw new InvalidOperationException("Could not download the clip. " + result.Tail());

        var downloaded = File.Exists(destination) ? destination : Directory.EnumerateFiles(workDir, "*.mp4").FirstOrDefault()
            ?? throw new InvalidOperationException("The clip video could not be found after download.");

        // yt-dlp bisa selesai dengan kode 0 tetapi meninggalkan mp4 kosong (hanya header); klip nyata selalu jauh lebih besar.
        var size = new FileInfo(downloaded).Length;
        if (size < 20_000)
            throw new InvalidOperationException($"The download produced an empty file ({size} bytes). yt-dlp log: {result.Tail(8)}");
        return downloaded;
    }

    private static void HandleLine(string line, double sectionLength, Action<JobProgress> report)
    {
        const string message = "Downloading the selected video segment...";

        if (line.StartsWith("CLIPPER|", StringComparison.Ordinal))
        {
            var fields = line.Split('|');
            if (fields.Length < 4) return;
            report(new JobProgress(
                ProgressKind.Download, message,
                ParsePercent(fields[1]), Clean(fields[2]), Clean(fields[3]),
                fields.Length > 4 ? Clean(fields[4]) : null));
            return;
        }

        // Cadangan: bila unduhan dipegang FFmpeg dan yt-dlp tidak memberi persen, baca statistik "time=" dari FFmpeg.
        var time = FfmpegTime.Match(line);
        if (!time.Success)
        {
            if (!string.IsNullOrWhiteSpace(line))
                report(JobProgress.Stage("yt-dlp: " + (line.Length > 300 ? line[..300] + "..." : line.Trim())));
            return;
        }
        var seconds = int.Parse(time.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
                      + int.Parse(time.Groups[2].Value, CultureInfo.InvariantCulture) * 60
                      + double.Parse(time.Groups[3].Value, CultureInfo.InvariantCulture);
        var speed = FfmpegSpeed.Match(line);
        report(new JobProgress(
            ProgressKind.Download, message,
            Math.Clamp(seconds / sectionLength * 100, 0, 100),
            speed.Success ? speed.Groups[1].Value + "x" : null));
    }

    private static double? ParsePercent(string text)
    {
        var match = NumberPattern.Match(text);
        if (!match.Success) return null;
        return double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0, 100)
            : null;
    }

    /// <summary>yt-dlp menulis "N/A", "NA" atau "Unknown..." untuk nilai yang belum diketahui.</summary>
    private static string? Clean(string text)
    {
        var value = text.Trim();
        if (value.Length == 0) return null;
        if (value.StartsWith("N/A", StringComparison.OrdinalIgnoreCase)) return null;
        if (value.StartsWith("NA", StringComparison.OrdinalIgnoreCase)) return null;
        if (value.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase)) return null;
        return value;
    }

    private static string Seconds(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record SubtitleChoice(string Language, bool IsAutomatic);
}

public sealed record SubtitleDownload(string Path, string Language, bool IsAutomatic);
