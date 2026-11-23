using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clipper98;

/// <summary>Reads and updates the data-only Python manifest without executing the imported file.</summary>
public static class ClipperJobScript
{
    private static readonly Regex Assignment = new(@"\ACLIPPER_JOB\s*=\s*", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static (ClipperJobManifest Manifest, List<ClipCandidate> Clips) Load(string path)
    {
        var source = File.ReadAllText(path).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        var assignment = Assignment.Match(source);
        if (!assignment.Success)
            throw new InvalidOperationException("File tidak memakai format CLIPPER_JOB. Pastikan skill @clipper terbaru membuat manifest Clipper 98.");

        var literal = source[assignment.Length..].Trim();
        try
        {
            using var document = JsonDocument.Parse(ConvertPythonLiterals(literal));
            var manifest = ParseManifest(document.RootElement);
            var clips = ValidateAndMap(manifest);
            return (manifest, clips);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Isi CLIPPER_JOB bukan data Python sederhana yang valid. File tidak dijalankan. Buat ulang hasilnya dengan skill @clipper terbaru.", ex);
        }
    }

    public static void Save(string path, ClipperJobManifest manifest, IReadOnlyCollection<ClipCandidate> clips)
    {
        manifest.ClipsData = clips.Select(clip => new ClipperJobClip
        {
            Id = clip.Name,
            StartSeconds = clip.Start,
            EndSeconds = clip.End,
            Selected = clip.IsSelected,
            Hook = clip.Hook,
            Reason = clip.Reason,
            Caption = clip.Caption,
            Hashtags = clip.Hashtags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
        }).ToList();

        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        var python = ConvertJsonLiterals(json);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, "CLIPPER_JOB = " + python + Environment.NewLine, new System.Text.UTF8Encoding(false));
        File.Move(tempPath, path, true);
    }

    private static ClipperJobManifest ParseManifest(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("CLIPPER_JOB harus berupa object.");
        var manifest = JsonSerializer.Deserialize<ClipperJobManifest>(root.GetRawText())
            ?? throw new JsonException("Manifest kosong.");
        if (manifest.SchemaVersion != 1) throw new InvalidOperationException($"Versi manifest {manifest.SchemaVersion} belum didukung.");
        if (!string.Equals(manifest.Source.Platform, "youtube", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Sumber manifest harus video YouTube.");
        if (!IsYouTubeUrl(manifest.Source.Url))
            throw new InvalidOperationException("URL YouTube di manifest tidak valid.");
        if (manifest.RequestedCandidateCount is < 1 or > 20)
            throw new InvalidOperationException("Jumlah kandidat di manifest harus antara 1 dan 20.");
        if (manifest.Constraints.MaxHeight is < 1 or > 1080)
            throw new InvalidOperationException("Resolusi manifest tidak boleh melebihi 1080p.");
        if (manifest.ClipsData.Count is < 1 or > 20)
            throw new InvalidOperationException("Manifest harus memiliki 1 sampai 20 kandidat.");
        return manifest;
    }

    private static List<ClipCandidate> ValidateAndMap(ClipperJobManifest manifest)
    {
        var clips = new List<ClipCandidate>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.ClipsData)
        {
            if (!Regex.IsMatch(item.Id, @"^(?:clip|custom)_\d{2}$", RegexOptions.IgnoreCase) || !ids.Add(item.Id))
                throw new InvalidOperationException("ID kandidat harus unik dan memakai pola clip_01 atau custom_01.");
            if (!double.IsFinite(item.StartSeconds) || !double.IsFinite(item.EndSeconds)
                || item.StartSeconds < 0 || item.EndSeconds <= item.StartSeconds
                || (manifest.Source.DurationSeconds is > 0 and var duration && item.EndSeconds > duration))
                throw new InvalidOperationException($"Timestamp kandidat {item.Id} tidak valid atau berada di luar durasi video.");

            clips.Add(new ClipCandidate
            {
                Name = item.Id,
                Start = item.StartSeconds,
                End = item.EndSeconds,
                IsSelected = item.Selected,
                Hook = item.Hook,
                Reason = item.Reason,
                Caption = item.Caption,
                Hashtags = string.Join(" ", item.Hashtags)
            });
        }

        for (var i = 0; i < clips.Count; i++)
            for (var j = i + 1; j < clips.Count; j++)
                if (clips[i].Start < clips[j].End && clips[j].Start < clips[i].End)
                    throw new InvalidOperationException($"Kandidat {clips[i].Name} dan {clips[j].Name} bertumpang tindih.");

        return clips;
    }

    private static string ConvertPythonLiterals(string source) => ConvertTokens(source, jsonToPython: false);
    private static string ConvertJsonLiterals(string source) => ConvertTokens(source, jsonToPython: true);

    private static string ConvertTokens(string source, bool jsonToPython)
    {
        var result = new System.Text.StringBuilder(source.Length + 16);
        var inString = false;
        var escaped = false;
        for (var i = 0; i < source.Length;)
        {
            var ch = source[i];
            if (inString)
            {
                result.Append(ch);
                i++;
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == '"') inString = false;
                continue;
            }
            if (ch == '"')
            {
                inString = true;
                result.Append(ch);
                i++;
                continue;
            }

            var tokens = jsonToPython
                ? new (string From, string To)[] { ("true", "True"), ("false", "False"), ("null", "None") }
                : new (string From, string To)[] { ("True", "true"), ("False", "false"), ("None", "null") };
            var replaced = false;
            foreach (var (from, to) in tokens)
            {
                if (i + from.Length > source.Length || !source.AsSpan(i, from.Length).SequenceEqual(from)) continue;
                var beforeOk = i == 0 || !char.IsLetterOrDigit(source[i - 1]) && source[i - 1] != '_';
                var afterIndex = i + from.Length;
                var afterOk = afterIndex == source.Length || !char.IsLetterOrDigit(source[afterIndex]) && source[afterIndex] != '_';
                if (!beforeOk || !afterOk) continue;
                result.Append(to);
                i = afterIndex;
                replaced = true;
                break;
            }
            if (!replaced)
            {
                result.Append(ch);
                i++;
            }
        }
        return result.ToString();
    }

    private static bool IsYouTubeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtu.be" or "www.youtube-nocookie.com";
}
