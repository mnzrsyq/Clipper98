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
            throw new InvalidOperationException("This file does not use the CLIPPER_JOB format. Use the current Clipper skill to create a manifest for Clipper98.");

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
            throw new InvalidOperationException("CLIPPER_JOB is not valid plain Python data. Clipper does not execute imported files. Recreate the manifest with the current @clipper skill.", ex);
        }
    }

    public static void Save(string path, ClipperJobManifest manifest, IReadOnlyCollection<ClipCandidate> clips)
    {
        if (manifest.Mode.Equals("condense", StringComparison.OrdinalIgnoreCase))
        {
            var first = clips.FirstOrDefault();
            manifest.KeepSegments = first?.KeptSegments ?? [];
            manifest.ClipsData = [];
            manifest.YouTubeTitle = first?.YouTubeTitle ?? string.Empty;
            manifest.YouTubeDescription = first?.YouTubeDescription ?? string.Empty;
            manifest.HeroFrameSeconds = first?.HeroFrameSeconds;
            manifest.ThumbnailHook = first?.ThumbnailHook ?? string.Empty;
            manifest.ThumbnailFont = first?.ThumbnailFontName ?? "Anton";
            manifest.OpeningMotionDurationSeconds = first?.OpeningMotionDurationSeconds ?? 2.0;
        }
        else
        {
            manifest.ClipsData = clips.Select(clip => new ClipperJobClip
            {
                Id = clip.Name,
                StartSeconds = clip.Start,
                EndSeconds = clip.End,
                HeroFrameSeconds = clip.HeroFrameSeconds,
                ThumbnailHook = clip.ThumbnailHook,
                ThumbnailFont = clip.ThumbnailFontName,
                OpeningMotionDurationSeconds = clip.OpeningMotionDurationSeconds,
                Selected = clip.IsSelected,
                Hook = clip.Hook,
                Reason = clip.Reason,
                Caption = clip.Caption,
                Hashtags = clip.Hashtags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                YouTubeTitle = clip.YouTubeTitle,
                YouTubeDescription = clip.YouTubeDescription
            }).ToList();
        }

        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        var python = ConvertJsonLiterals(json);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, "CLIPPER_JOB = " + python + Environment.NewLine, new System.Text.UTF8Encoding(false));
        File.Move(tempPath, path, true);
    }

    private static ClipperJobManifest ParseManifest(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("CLIPPER_JOB must be an object.");
        var manifest = JsonSerializer.Deserialize<ClipperJobManifest>(root.GetRawText())
            ?? throw new JsonException("The manifest is empty.");
        if (manifest.SchemaVersion != 1) throw new InvalidOperationException($"Manifest version {manifest.SchemaVersion} is not supported.");
        if (manifest.Mode is null || manifest.KeepSegments is null || manifest.ClipsData is null)
            throw new InvalidOperationException("The manifest mode or segment list is invalid.");
        if (!string.Equals(manifest.Source.Platform, "youtube", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The manifest source must be a YouTube video.");
        if (!IsYouTubeUrl(manifest.Source.Url))
            throw new InvalidOperationException("The YouTube URL in the manifest is invalid.");
        if (manifest.Constraints.MaxHeight is < 1 or > 1080)
            throw new InvalidOperationException("Manifest resolution cannot exceed 1080p.");
        if (string.Equals(manifest.Mode, "condense", StringComparison.OrdinalIgnoreCase))
        {
            if (manifest.KeepSegments.Count is < 1 or > 100)
                throw new InvalidOperationException("A condensed video must contain 1 to 100 retained segments.");
            if (manifest.TargetDurationSeconds < 0)
                throw new InvalidOperationException("The output duration target is invalid.");
        }
        else
        {
            if (!manifest.Mode.Equals("short_clips", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The manifest mode must be short_clips or condense.");
            if (manifest.RequestedCandidateCount is < 1 or > 20)
                throw new InvalidOperationException("The manifest must request between 1 and 20 candidates.");
            if (manifest.ClipsData.Count is < 1 or > 20)
                throw new InvalidOperationException("The manifest must contain 1 to 20 candidates.");
        }
        return manifest;
    }

    private static List<ClipCandidate> ValidateAndMap(ClipperJobManifest manifest)
    {
        if (string.Equals(manifest.Mode, "condense", StringComparison.OrdinalIgnoreCase))
        {
            var orderedSegments = manifest.KeepSegments.OrderBy(segment => segment.StartSeconds).ToList();
            var previousEnd = 0d;
            foreach (var segment in orderedSegments)
            {
                if (!double.IsFinite(segment.StartSeconds) || !double.IsFinite(segment.EndSeconds)
                    || segment.StartSeconds < 0 || segment.EndSeconds <= segment.StartSeconds
                    || segment.StartSeconds < previousEnd
                    || string.IsNullOrWhiteSpace(segment.Reason)
                    || (segment.Narration?.Length ?? 0) > 1600
                    || !double.IsFinite(segment.NarrationOffsetSeconds)
                    || segment.NarrationOffsetSeconds < 0
                    || segment.NarrationOffsetSeconds > segment.EndSeconds - segment.StartSeconds
                    || (manifest.Source.DurationSeconds is > 0 and var sourceDuration && segment.EndSeconds > sourceDuration))
                    throw new InvalidOperationException("Condensed video segments need a reason, must be ordered and non-overlapping, and must fall within the source video duration.");
                previousEnd = segment.EndSeconds;
            }

            var keptDuration = orderedSegments.Sum(segment => segment.DurationSeconds);
            if (manifest.YouTubeTitle?.Length > 100)
                throw new InvalidOperationException("YouTube titles in the manifest cannot exceed 100 characters.");
            if (manifest.YouTubeDescription?.Length > 5000)
                throw new InvalidOperationException("YouTube descriptions in the manifest cannot exceed 5,000 characters.");
            var lead = orderedSegments[0].StartSeconds;
            var tail = orderedSegments[^1].EndSeconds;
            return
            [
                new ClipCandidate
                {
                    Name = "video_ringkas",
                    Start = lead,
                    End = tail,
                    HeroFrameSeconds = manifest.HeroFrameSeconds ?? lead,
                    ThumbnailHook = !string.IsNullOrWhiteSpace(manifest.ThumbnailHook)
                        ? manifest.ThumbnailHook
                        : (string.IsNullOrWhiteSpace(manifest.CondensedSummary) ? "RINGKASAN VIDEO" : manifest.CondensedSummary),
                    ThumbnailFontName = !string.IsNullOrWhiteSpace(manifest.ThumbnailFont) ? manifest.ThumbnailFont : "Anton",
                    OpeningMotionDurationSeconds = manifest.OpeningMotionDurationSeconds ?? 2.0,
                    Hook = string.IsNullOrWhiteSpace(manifest.CondensedSummary) ? "Condensed video" : manifest.CondensedSummary,
                    Reason = $"{orderedSegments.Count} segments retained · output about {TimeSpan.FromSeconds(keptDuration):hh\\:mm\\:ss}",
                    KeptSegments = orderedSegments,
                    YouTubeTitle = manifest.YouTubeTitle ?? string.Empty,
                    YouTubeDescription = manifest.YouTubeDescription ?? string.Empty
                }
            ];
        }

        var clips = new List<ClipCandidate>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.ClipsData)
        {
            if (!Regex.IsMatch(item.Id, @"^(?:clip|custom)_\d{2}$", RegexOptions.IgnoreCase) || !ids.Add(item.Id))
                throw new InvalidOperationException("Candidate IDs must be unique and follow the clip_01 or custom_01 format.");
            if (!double.IsFinite(item.StartSeconds) || !double.IsFinite(item.EndSeconds)
                || item.StartSeconds < 0 || item.EndSeconds <= item.StartSeconds
                || (manifest.Source.DurationSeconds is > 0 and var duration && item.EndSeconds > duration))
                throw new InvalidOperationException($"Candidate {item.Id} has invalid timestamps or falls outside the video duration.");

            clips.Add(new ClipCandidate
            {
                Name = item.Id,
                Start = item.StartSeconds,
                End = item.EndSeconds,
                HeroFrameSeconds = item.HeroFrameSeconds ?? (item.StartSeconds + (item.EndSeconds - item.StartSeconds) / 2.0),
                ThumbnailHook = !string.IsNullOrWhiteSpace(item.ThumbnailHook) ? item.ThumbnailHook : item.Hook,
                ThumbnailFontName = !string.IsNullOrWhiteSpace(item.ThumbnailFont) ? item.ThumbnailFont : "Anton",
                OpeningMotionDurationSeconds = item.OpeningMotionDurationSeconds ?? 2.0,
                IsSelected = item.Selected,
                Hook = item.Hook,
                Reason = item.Reason,
                Caption = item.Caption,
                Hashtags = string.Join(" ", item.Hashtags),
                YouTubeTitle = item.YouTubeTitle,
                YouTubeDescription = item.YouTubeDescription
            });
        }

        for (var i = 0; i < clips.Count; i++)
            for (var j = i + 1; j < clips.Count; j++)
                if (clips[i].Start < clips[j].End && clips[j].Start < clips[i].End)
                    throw new InvalidOperationException($"Candidates {clips[i].Name} and {clips[j].Name} overlap.");

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
