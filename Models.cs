namespace Clipper98;

using System.Text.Json.Serialization;

public sealed class SubtitleCue
{
    public double Start { get; init; }
    public double End { get; init; }
    public string Text { get; init; } = string.Empty;
    public List<SubtitleWord> Words { get; init; } = [];
}

public sealed class SubtitleWord
{
    public double Start { get; init; }
    public double End { get; init; }
    public string Text { get; init; } = string.Empty;
}

public sealed class ClipCandidate
{
    public bool IsSelected { get; set; } = true;
    public string Name { get; init; } = string.Empty;
    public double Start { get; set; }
    public double End { get; set; }
    public string Hook { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string Caption { get; init; } = string.Empty;
    public string Hashtags { get; init; } = string.Empty;
    public bool PreserveFullFrame { get; set; }
    public string TimeRange => $"{TimeSpan.FromSeconds(Start):hh\\:mm\\:ss} - {TimeSpan.FromSeconds(End):hh\\:mm\\:ss}";
}

public sealed class ClipperJobManifest
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("source")] public ClipperJobSource Source { get; set; } = new();
    [JsonPropertyName("requested_candidate_count")] public int RequestedCandidateCount { get; set; } = 5;
    [JsonPropertyName("constraints")] public ClipperJobConstraints Constraints { get; set; } = new();
    [JsonPropertyName("clips_data")] public List<ClipperJobClip> ClipsData { get; set; } = [];
}

public sealed class ClipperJobSource
{
    [JsonPropertyName("platform")] public string Platform { get; set; } = "youtube";
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("video_id")] public string VideoId { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = "Video YouTube";
    [JsonPropertyName("duration_seconds")] public double? DurationSeconds { get; set; }
}

public sealed class ClipperJobConstraints
{
    [JsonPropertyName("min_duration_seconds")] public int MinDurationSeconds { get; set; }
    [JsonPropertyName("max_duration_seconds")] public int MaxDurationSeconds { get; set; }
    [JsonPropertyName("max_height")] public int MaxHeight { get; set; } = 1080;
}

public sealed class ClipperJobClip
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("start_seconds")] public double StartSeconds { get; set; }
    [JsonPropertyName("end_seconds")] public double EndSeconds { get; set; }
    [JsonPropertyName("selected")] public bool Selected { get; set; } = true;
    [JsonPropertyName("hook")] public string Hook { get; set; } = string.Empty;
    [JsonPropertyName("reason")] public string Reason { get; set; } = string.Empty;
    [JsonPropertyName("caption")] public string Caption { get; set; } = string.Empty;
    [JsonPropertyName("hashtags")] public List<string> Hashtags { get; set; } = [];
}

public sealed class RenderJobRequest
{
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("clip")] public ClipperJobClip Clip { get; set; } = new();
    [JsonPropertyName("caption_style")] public CaptionStyle CaptionStyle { get; set; } = new();
    [JsonPropertyName("output_folder")] public string OutputFolder { get; set; } = string.Empty;
    [JsonPropertyName("language")] public string Language { get; set; } = "id";
    [JsonPropertyName("subtitle_source")] public string SubtitleSource { get; set; } = "auto";
    [JsonPropertyName("frame_png_path")] public string FramePngPath { get; set; } = string.Empty;
    [JsonPropertyName("preserve_full_frame")] public bool PreserveFullFrame { get; set; }
    [JsonPropertyName("progress_file")] public string ProgressFile { get; set; } = string.Empty;
}

public sealed class CaptionStyle
{
    public string FontName { get; set; } = "Segoe UI";
    public string FontFilePath { get; set; } = string.Empty;
    public bool FontBold { get; set; } = true;
    public bool FontItalic { get; set; }
    public int FontSize { get; set; } = 100;
    public double LetterSpacing { get; set; }
    public int LineSpacing { get; set; } = 10;
    public string TextColor { get; set; } = "White";
    public string HighlightColor { get; set; } = "Yellow";
    public int Outline { get; set; } = 5;
    public bool HighlightEnabled { get; set; } = true;
    public int PositionY { get; set; } = 1420;
    public int MaxLines { get; set; } = 2;
    public string TextCase { get; set; } = "sentence";
}

public sealed class AppSettings
{
    public string OutputFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Clipper98");
    public string SubtitleSource { get; set; } = "auto";
    public List<SubtitleStylePreset> SubtitlePresets { get; set; } = [];
    public string FramePngPath { get; set; } = string.Empty;
}

public sealed class SubtitleStylePreset
{
    public string Name { get; set; } = string.Empty;
    public CaptionStyle Style { get; set; } = new();
}

public sealed class ScanResult
{
    public string Title { get; init; } = string.Empty;
    public double Duration { get; init; }
    public string TranscriptSource { get; init; } = string.Empty;
}

public enum ProgressKind { Stage, Download }

/// <summary>Laporan progres dari backend ke UI. Stage = pesan tahap (masuk log); Download = angka unduhan (hanya status bar).</summary>
public sealed record JobProgress(
    ProgressKind Kind,
    string Message,
    double? Percent = null,
    string? Speed = null,
    string? Eta = null,
    string? Size = null)
{
    public static JobProgress Stage(string message) => new(ProgressKind.Stage, message);
}
