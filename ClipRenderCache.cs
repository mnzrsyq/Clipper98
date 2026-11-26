using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clipper98;

/// <summary>Persistent, style-independent cache for selected video sections and their subtitle cues.</summary>
public sealed class ClipRenderCache
{
    private const string CacheVersion = "v3";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clipper98", "Cache");

    public FileStream AcquireActiveLock()
    {
        var lockFolder = Path.Combine(_root, "active", Guid.NewGuid().ToString("N"));
        return TempCacheService.AcquireActiveLock(lockFolder);
    }

    public string GetVideoPath(string url, double start, double end) =>
        Path.Combine(_root, "video", CreateKey(url, start, end, "1080p-avc-v1") + ".mp4");

    public string GetJoinedSegmentsPath(string url, IReadOnlyList<CondensedSegment> segments)
    {
        var signature = string.Join(';', segments.Select(segment =>
            $"{Math.Round(segment.StartSeconds * 1000)}-{Math.Round(segment.EndSeconds * 1000)}"));
        return Path.Combine(_root, "video", CreateKey(url, 0, 0, "joined-v1|" + signature) + ".mp4");
    }

    public string GetFramingPath(string videoPath, int cropHeight)
    {
        var info = new FileInfo(videoPath);
        var material = string.Join('|', CacheVersion, Path.GetFullPath(videoPath), info.Length,
            info.LastWriteTimeUtc.Ticks, cropHeight);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        return Path.Combine(_root, "framing", key + ".json");
    }

    public bool TryGetFraming(string path, out SmartCropAnalysis analysis)
    {
        analysis = new SmartCropAnalysis(false, false, null, []);
        try
        {
            if (!File.Exists(path)) return false;
            analysis = JsonSerializer.Deserialize<SmartCropAnalysis>(File.ReadAllText(path), JsonOptions)
                ?? new SmartCropAnalysis(false, false, null, []);
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            analysis = new SmartCropAnalysis(false, false, null, []);
            return false;
        }
    }

    public void StoreFraming(string path, SmartCropAnalysis analysis)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(analysis));
            File.Move(temporary, path, overwrite: true);
        }
        finally { TryDelete(temporary); }
    }

    public bool TryGetVideo(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length >= 20_000; }
        catch (IOException) { return false; }
    }

    public void StoreVideo(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary, overwrite: false);
            if (new FileInfo(temporary).Length < 20_000) return;
            File.Move(temporary, destination, overwrite: true);
        }
        finally { TryDelete(temporary); }
    }

    public string GetTranscriptPath(
        string url, double start, double end, string source, string language, string whisperModel) =>
        Path.Combine(_root, "transcript", CreateKey(
            url, start, end, $"{source}|{language}|{whisperModel}") + ".json");

    public string GetTranscriptPathForSegments(
        string url, IReadOnlyList<CondensedSegment> segments, string source, string language, string whisperModel)
    {
        var signature = string.Join(';', segments.Select(segment =>
            $"{Math.Round(segment.StartSeconds * 1000)}-{Math.Round(segment.EndSeconds * 1000)}"));
        return Path.Combine(_root, "transcript", CreateKey(url, 0, 0,
            $"{source}|{language}|{whisperModel}|segments:{signature}") + ".json");
    }

    public bool TryGetTranscript(string path, out List<SubtitleCue> cues)
    {
        cues = [];
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0) return false;
            cues = JsonSerializer.Deserialize<List<SubtitleCue>>(File.ReadAllText(path), JsonOptions) ?? [];
            return cues.Count > 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            cues = [];
            return false;
        }
    }

    public void StoreTranscript(string path, IReadOnlyList<SubtitleCue> cues)
    {
        if (cues.Count == 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(cues));
            File.Move(temporary, path, overwrite: true);
        }
        finally { TryDelete(temporary); }
    }

    private static string CreateKey(string url, double start, double end, string variation)
    {
        var videoId = GetVideoId(url);
        var material = string.Join('|', CacheVersion, videoId,
            Math.Round(start * 1000), Math.Round(end * 1000), variation);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    private static string GetVideoId(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
                return uri.AbsolutePath.Trim('/').Split('/').FirstOrDefault() ?? url;
            var queryId = System.Web.HttpUtility.ParseQueryString(uri.Query)["v"];
            if (!string.IsNullOrWhiteSpace(queryId)) return queryId;
            var match = Regex.Match(uri.AbsolutePath, @"/(?:shorts|embed|live)/([^/?]+)", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value;
        }
        return url.Trim();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
