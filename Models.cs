namespace Clipper98;

using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;

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
    public string YouTubeTitle { get; set; } = string.Empty;
    public string YouTubeDescription { get; set; } = string.Empty;
    public double? HeroFrameSeconds { get; set; }
    public string ThumbnailHook { get; set; } = string.Empty;
    public bool EnableOpeningMotion { get; set; }
    public double OpeningMotionDurationSeconds { get; set; } = 2.0;
    public string ThumbnailFontName { get; set; } = "Anton";
    public bool PreserveFullFrame { get; set; }
    public List<CondensedSegment> KeptSegments { get; set; } = [];
    public string DisplayTitle => !string.IsNullOrWhiteSpace(YouTubeTitle) ? YouTubeTitle : (!string.IsNullOrWhiteSpace(Hook) ? Hook : Name);
    public string TimeRange => $"{TimeSpan.FromSeconds(Start):hh\\:mm\\:ss} - {TimeSpan.FromSeconds(End):hh\\:mm\\:ss}";
}

public sealed class CondensedSegment
{
    [JsonPropertyName("start_seconds")] public double StartSeconds { get; set; }
    [JsonPropertyName("end_seconds")] public double EndSeconds { get; set; }
    [JsonPropertyName("reason")] public string Reason { get; set; } = string.Empty;
    [JsonPropertyName("narration")] public string Narration { get; set; } = string.Empty;
    [JsonPropertyName("narration_offset_seconds")] public double NarrationOffsetSeconds { get; set; }
    [JsonIgnore] public double DurationSeconds => EndSeconds - StartSeconds;
    [JsonIgnore] public string TimeRange => $"{TimeSpan.FromSeconds(StartSeconds):hh\\:mm\\:ss} - {TimeSpan.FromSeconds(EndSeconds):hh\\:mm\\:ss}";
}

public sealed class ClipperJobManifest
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("mode")] public string Mode { get; set; } = "short_clips";
    [JsonPropertyName("source")] public ClipperJobSource Source { get; set; } = new();
    [JsonPropertyName("requested_candidate_count")] public int RequestedCandidateCount { get; set; } = 5;
    [JsonPropertyName("target_duration_seconds")] public int TargetDurationSeconds { get; set; }
    [JsonPropertyName("condensed_summary")] public string CondensedSummary { get; set; } = string.Empty;
    [JsonPropertyName("youtube_title")] public string YouTubeTitle { get; set; } = string.Empty;
    [JsonPropertyName("youtube_description")] public string YouTubeDescription { get; set; } = string.Empty;
    [JsonPropertyName("hero_frame_seconds")] public double? HeroFrameSeconds { get; set; }
    [JsonPropertyName("thumbnail_hook")] public string? ThumbnailHook { get; set; }
    [JsonPropertyName("thumbnail_font")] public string? ThumbnailFont { get; set; }
    [JsonPropertyName("opening_motion_duration_seconds")] public double? OpeningMotionDurationSeconds { get; set; }
    [JsonPropertyName("keep_segments")] public List<CondensedSegment> KeepSegments { get; set; } = [];
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
    [JsonPropertyName("hero_frame_seconds")] public double? HeroFrameSeconds { get; set; }
    [JsonPropertyName("thumbnail_hook")] public string? ThumbnailHook { get; set; }
    [JsonPropertyName("thumbnail_font")] public string? ThumbnailFont { get; set; }
    [JsonPropertyName("opening_motion_duration_seconds")] public double? OpeningMotionDurationSeconds { get; set; }
    [JsonPropertyName("selected")] public bool Selected { get; set; } = true;
    [JsonPropertyName("hook")] public string Hook { get; set; } = string.Empty;
    [JsonPropertyName("reason")] public string Reason { get; set; } = string.Empty;
    [JsonPropertyName("caption")] public string Caption { get; set; } = string.Empty;
    [JsonPropertyName("hashtags")] public List<string> Hashtags { get; set; } = [];
    [JsonPropertyName("youtube_title")] public string YouTubeTitle { get; set; } = string.Empty;
    [JsonPropertyName("youtube_description")] public string YouTubeDescription { get; set; } = string.Empty;
}

public sealed class RenderJobRequest
{
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("clip")] public ClipperJobClip Clip { get; set; } = new();
    [JsonPropertyName("segments")] public List<CondensedSegment> Segments { get; set; } = [];
    [JsonPropertyName("hero_frame_seconds")] public double? HeroFrameSeconds { get; set; }
    [JsonPropertyName("thumbnail_hook")] public string? ThumbnailHook { get; set; }
    [JsonPropertyName("thumbnail_font")] public string? ThumbnailFont { get; set; }
    [JsonPropertyName("enable_opening_motion")] public bool EnableOpeningMotion { get; set; }
    [JsonPropertyName("opening_motion_duration_seconds")] public double OpeningMotionDurationSeconds { get; set; } = 2.0;
    [JsonPropertyName("use_narration")] public bool UseNarration { get; set; }
    [JsonPropertyName("tts_options")] public TtsOptions TtsOptions { get; set; } = new();
    [JsonPropertyName("use_subtitles")] public bool UseSubtitles { get; set; } = true;
    [JsonPropertyName("preserve_source_aspect_ratio")] public bool PreserveSourceAspectRatio { get; set; }
    [JsonPropertyName("use_four_by_five_content_frame")] public bool UseFourByFiveContentFrame { get; set; }
    [JsonPropertyName("caption_style")] public CaptionStyle CaptionStyle { get; set; } = new();
    [JsonPropertyName("output_folder")] public string OutputFolder { get; set; } = string.Empty;
    [JsonPropertyName("language")] public string Language { get; set; } = "id";
    [JsonPropertyName("subtitle_source")] public string SubtitleSource { get; set; } = "auto";
    [JsonPropertyName("frame_png_path")] public string FramePngPath { get; set; } = string.Empty;
    [JsonPropertyName("preserve_full_frame")] public bool PreserveFullFrame { get; set; }
    [JsonPropertyName("progress_file")] public string ProgressFile { get; set; } = string.Empty;
}

public sealed class TtsOptions
{
    [JsonPropertyName("provider")] public string Provider { get; set; } = "fish";
    [JsonPropertyName("voice")] public string Voice { get; set; } = "budi";
    [JsonPropertyName("rate_percent")] public int RatePercent { get; set; }
    [JsonPropertyName("volume_percent")] public int VolumePercent { get; set; } = 100;
    [JsonPropertyName("original_volume_percent")] public int OriginalVolumePercent { get; set; } = 55;
    [JsonPropertyName("pitch_cents")] public int PitchCents { get; set; }
}

public sealed record NarrationTrack(string FilePath, double StartSeconds);

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
    public string HighlightMode { get; set; } = "color";
    public int Outline { get; set; } = 5;
    public bool HighlightEnabled { get; set; } = true;
    public bool WordByWord { get; set; }
    public int PositionY { get; set; } = 1420;
    public int MaxLines { get; set; } = 2;
    public string TextCase { get; set; } = "sentence";
}

public sealed class AppSettings
{
    public string OutputFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Clipper98");
    public string SubtitleSource { get; set; } = "auto";
    public bool UseShortClipSubtitles { get; set; } = true;
    public List<SubtitleStylePreset> SubtitlePresets { get; set; } = [];
    public string FramePngPath { get; set; } = string.Empty;
    public bool UseNarration { get; set; }
    public string TtsProvider { get; set; } = "fish";
    public string TtsVoice { get; set; } = "budi";
    public string FishApiKey { get; set; } = string.Empty;
    public int TtsRatePercent { get; set; }
    public int TtsVolumePercent { get; set; } = 100;
    public int OriginalVolumePercent { get; set; } = 55;
    public int TtsPitchCents { get; set; }
    public bool NightMode { get; set; }
    public bool EnableOpeningMotion { get; set; }
    public double OpeningMotionDurationSeconds { get; set; } = 2.0;
    public string ThumbnailFontName { get; set; } = "Anton";
    public string YouTubeClientSecretsJson { get; set; } = string.Empty;
    public List<YouTubeUploadItem> YouTubeUploadQueue { get; set; } = [];
    public DateTimeOffset? LastUploadedPublishAt { get; set; }
    public string LastUploadedVideoId { get; set; } = string.Empty;
    public List<string> CustomWeekdaySlots { get; set; } = [];
    public List<string> CustomWeekendSlots { get; set; } = [];
}

public sealed class YouTubeUploadItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _title = string.Empty;
    private string _description = string.Empty;
    private DateTimeOffset? _publishAt;
    private string _uploadStatus = "Queued";

    public string FilePath { get; set; } = string.Empty;
    public string ClipName { get; set; } = string.Empty;
    public string YouTubeVideoId { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

    public DateTimeOffset? PublishAt
    {
        get => _publishAt;
        set
        {
            if (SetField(ref _publishAt, value))
                OnPropertyChanged(nameof(PublishAtDisplay));
        }
    }

    public string UploadStatus
    {
        get => _uploadStatus;
        set => SetField(ref _uploadStatus, value);
    }

    [JsonIgnore]
    public string FileName => Path.GetFileName(FilePath);

    [JsonIgnore]
    public int QueueNumber { get; set; }

    [JsonIgnore]
    public string PublishAtDisplay => PublishAt is { } publishAt
        ? $"{publishAt.ToString("ddd, MMM d · h:mm tt", CultureInfo.GetCultureInfo("en-US"))} WIB"
        : "Not scheduled";

    [JsonIgnore]
    public string FileState => File.Exists(FilePath) ? "Found" : "Missing";

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
