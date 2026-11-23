using System.Text.RegularExpressions;

namespace Clipper98;

/// <summary>Menyambungkan tahap lokal: unduh bagian, transkrip, caption, dan render.</summary>
public sealed class ClipPipeline
{
    private readonly string _workRoot = Path.Combine(Path.GetTempPath(), "Clipper98");
    private readonly YtDlpService _ytDlp = new();
    private readonly WhisperService _whisper = new();
    private readonly ClipRenderer _renderer = new();
    private readonly ClipRenderCache _cache = new();

    public async Task<string> RenderAsync(
        string url, ScanResult scan, ClipCandidate clip, CaptionStyle style,
        string outputFolder, string language, string subtitleSource, string whisperModel, string framePngPath,
        IProgress<JobProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(outputFolder);
        var job = Path.Combine(_workRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        using var activeLock = TempCacheService.AcquireActiveLock(job);
        using var cacheLock = _cache.AcquireActiveLock();

        var output = Path.Combine(outputFolder, BuildFileName(scan.Title, clip) + ".mp4");

        var cachedVideo = _cache.GetVideoPath(url, clip.Start, clip.End);
        string source;
        if (_cache.TryGetVideo(cachedVideo))
        {
            source = cachedVideo;
            progress.Report(JobProgress.Stage("Cache video cocok; melewati unduhan."));
        }
        else
        {
            progress.Report(JobProgress.Stage("Mengunduh bagian video terpilih..."));
            source = await _ytDlp.DownloadSectionAsync(
                url, clip.Start, clip.End, Path.Combine(job, "source.mp4"), job, p => progress.Report(p), ct);
            try
            {
                _cache.StoreVideo(source, cachedVideo);
                source = cachedVideo;
                progress.Report(JobProgress.Stage("Segmen video disimpan ke cache lokal."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                progress.Report(JobProgress.Stage("Cache video gagal disimpan; render tetap dilanjutkan."));
            }
        }

        var cues = new List<SubtitleCue>();
        string? subtitleProblem = null;
        var transcriptCache = _cache.GetTranscriptPath(url, clip.Start, clip.End, subtitleSource, language, whisperModel);
        var hasTranscriptCache = _cache.TryGetTranscript(transcriptCache, out cues);
        if (hasTranscriptCache)
        {
            progress.Report(JobProgress.Stage("Cache transkrip cocok; melewati pengambilan subtitle dan transkripsi."));
        }
        else if (subtitleSource.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            progress.Report(JobProgress.Stage("Mencari subtitle YouTube..."));
            try
            {
                var subtitle = await _ytDlp.DownloadSubtitleAsync(url, language, job, ct);
                if (subtitle is not null)
                {
                    var sourceCues = SubtitleParser.ParseFile(subtitle.Path);
                    cues = SubtitleParser.TrimToRange(sourceCues, clip.Start, clip.End);
                    if (cues.Count > 0)
                    {
                        var sourceType = subtitle.IsAutomatic ? "otomatis" : "manual";
                        progress.Report(JobProgress.Stage($"Memakai subtitle YouTube {subtitle.Language} ({sourceType})."));
                    }
                    else subtitleProblem = "Subtitle YouTube tidak berisi teks pada rentang klip ini.";
                }
                else subtitleProblem = "Subtitle YouTube untuk bahasa yang dipilih tidak tersedia.";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                subtitleProblem = "Subtitle YouTube gagal diambil: " + ex.Message;
            }
        }

        if (cues.Count == 0)
        {
            var message = subtitleSource.Equals("whisper", StringComparison.OrdinalIgnoreCase)
                ? $"Memakai Whisper {WhisperDisplayName(whisperModel)} untuk transkrip lokal..."
                : $"{subtitleProblem ?? "Subtitle YouTube tidak tersedia."} Melanjutkan dengan Whisper {WhisperDisplayName(whisperModel)}...";
            progress.Report(JobProgress.Stage(message));
            cues = await _whisper.TranscribeAsync(source, language, whisperModel, job, ct);
        }

        if (!hasTranscriptCache)
        {
            try { _cache.StoreTranscript(transcriptCache, cues); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { progress.Report(JobProgress.Stage("Cache transkrip gagal disimpan; render tetap dilanjutkan.")); }
        }

        var assFile = Path.Combine(job, "caption.ass");
        CaptionWriter.WriteAss(assFile, cues, style);

        string? cropExpression = null;
        IReadOnlyList<SmartCropSegment> fullFrameSegments = [];
        if (!clip.PreserveFullFrame)
        {
            progress.Report(JobProgress.Stage("Menganalisis wajah untuk smart crop..."));
            var analysis = await new SmartCropAnalyzer().AnalyzeAsync(source, job, ct);
            cropExpression = analysis.CropExpression;
            fullFrameSegments = analysis.FullFrameSegments;
            var fallbackSummary = fullFrameSegments.Count == 0
                ? string.Empty
                : $" {fullFrameSegments.Count} rentang tanpa wajah akan memakai video utuh dengan latar blur.";
            progress.Report(JobProgress.Stage(!analysis.IsAvailable
                ? "Analisis wajah tidak tersedia; memakai crop tengah sebagai cadangan."
                : !analysis.HasDetectedFace
                    ? "Wajah tidak terdeteksi; seluruh klip memakai video utuh dengan latar blur."
                    : $"Wajah utama terlacak; smart crop aktif.{fallbackSummary}"));
        }

        progress.Report(JobProgress.Stage("Membuat video 9:16, frame, dan caption..."));
        try
        {
            await _renderer.RenderAsync(source, assFile, output, job, cropExpression, clip.PreserveFullFrame,
                fullFrameSegments, framePngPath, style.FontFilePath, ct);
        }
        catch
        {
            TryDeleteFile(output); // jangan tinggalkan mp4 setengah jadi
            throw;
        }

        activeLock.Dispose();
        TryDeleteFolder(job); // hanya folder kerja milik job ini, hanya bila sukses
        return output;
    }

    private static string WhisperDisplayName(string model) => model switch
    {
        "large-v3-turbo" => "Large-v3-Turbo reguler",
        "large-v3-turbo-q8_0" => "Large-v3-Turbo Q8_0",
        _ => "Large-v3-Turbo reguler"
    };

    /// <summary>Nama unik per video + klip + rentang waktu, supaya scan video lain tidak menimpa hasil sebelumnya.</summary>
    private static string BuildFileName(string title, ClipCandidate clip)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var slug = new string(title.Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '_' : c).ToArray());
        slug = Regex.Replace(slug, "_+", "_").Trim('_', '.');
        if (slug.Length > 40) slug = slug[..40].TrimEnd('_', '.');
        if (slug.Length == 0) slug = "video";
        return $"{slug}_{clip.Name}_{TimeSpan.FromSeconds(clip.Start):hhmmss}-{TimeSpan.FromSeconds(clip.End):hhmmss}";
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteFolder(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }
}
