using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clipper98;

/// <summary>Persistent, style-independent cache for selected video sections and their subtitle cues.</summary>
public sealed class ClipRenderCache
{
    private const string CacheVersion = "v2";
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
