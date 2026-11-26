using System.Text.RegularExpressions;

namespace Clipper98;

/// <summary>Menyambungkan tahap lokal: unduh bagian, transkrip, caption, dan render.</summary>
public sealed class ClipPipeline
{
    private readonly string _workRoot = Path.Combine(Path.GetTempPath(), "Clipper98");
    private readonly YtDlpService _ytDlp = new();
    private readonly WhisperService _whisper = new();
    private readonly ClipRenderer _renderer = new();
    private readonly TtsService _tts = new();
    private readonly ClipRenderCache _cache = new();

    public async Task<string> RenderAsync(
        string url, ScanResult scan, ClipCandidate clip, CaptionStyle style,
        string outputFolder, string language, string subtitleSource, string framePngPath,
        bool useSubtitles, bool preserveSourceAspectRatio, bool useFourByFiveContentFrame,
        bool useNarration, TtsOptions ttsOptions, IProgress<JobProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(outputFolder);
        var job = Path.Combine(_workRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        using var activeLock = TempCacheService.AcquireActiveLock(job);
        using var cacheLock = _cache.AcquireActiveLock();

        var output = Path.Combine(outputFolder, BuildFileName(scan.Title, clip) + ".mp4");

        string source;
        if (clip.KeptSegments.Count > 0)
        {
            source = await DownloadAndJoinSegmentsAsync(url, clip.KeptSegments, job, progress, ct);
        }
        else
        {
            var cachedVideo = _cache.GetVideoPath(url, clip.Start, clip.End);
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
        }

        var narrationTracks = new List<NarrationTrack>();
        var narrationCues = new List<SubtitleCue>();
        if (useNarration)
        {
            if (clip.KeptSegments.Count == 0)
                throw new InvalidOperationException("Narasi VO hanya tersedia untuk mode video ringkas.");
            progress.Report(JobProgress.Stage("Menyiapkan narasi untuk potongan video..."));
            var cursor = 0d;
            var previousVoiceEnd = 0d;
            foreach (var segment in clip.KeptSegments)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(segment.Narration))
                    throw new InvalidOperationException("Ada bagian tanpa teks narasi. Perbarui manifest Gemini atau matikan opsi narasi.");
                var audio = await _tts.GenerateAsync(segment.Narration, ttsOptions, progress, ct);
                var media = await MediaProbe.ProbeAsync(audio, ct);
                if (media.DurationSeconds is not > 0)
                    throw new InvalidOperationException("Durasi audio narasi tidak dapat dibaca.");
                var offset = Math.Clamp(segment.NarrationOffsetSeconds, 0, Math.Max(0, segment.DurationSeconds));
                var start = cursor + offset;
                if (start < previousVoiceEnd) start = previousVoiceEnd + 0.12;
                narrationTracks.Add(new NarrationTrack(audio, start));
                previousVoiceEnd = start + media.DurationSeconds.Value;
                narrationCues.Add(new SubtitleCue
                {
                    Start = start,
                    End = previousVoiceEnd,
                    Text = segment.Narration.Trim()
                });
                cursor += segment.DurationSeconds;
            }
        }

        var cues = new List<SubtitleCue>();
        string? subtitleProblem = null;
        const string whisperModel = "large-v3-turbo";
        var isCondensed = clip.KeptSegments.Count > 0;
        var transcriptCache = isCondensed
            ? _cache.GetTranscriptPathForSegments(url, clip.KeptSegments, subtitleSource, language, whisperModel)
            : _cache.GetTranscriptPath(url, clip.Start, clip.End, subtitleSource, language, whisperModel);
        var hasTranscriptCache = useSubtitles && !useNarration && _cache.TryGetTranscript(transcriptCache, out cues);
        if (!useSubtitles)
        {
            progress.Report(JobProgress.Stage("Subtitle dilewati sesuai pilihan."));
        }
        else if (useNarration)
        {
            cues = narrationCues;
            progress.Report(JobProgress.Stage("Memakai subtitle dari teks narasi Gemini agar sesuai dengan VO."));
        }
        else if (hasTranscriptCache)
        {
            progress.Report(JobProgress.Stage("Cache transkrip cocok; melewati pengambilan subtitle dan transkripsi."));
        }
        else if (!isCondensed && subtitleSource.Equals("auto", StringComparison.OrdinalIgnoreCase))
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

        if (useSubtitles && !useNarration && cues.Count == 0)
        {
            var message = subtitleSource.Equals("whisper", StringComparison.OrdinalIgnoreCase)
                ? "Memakai Whisper Large-v3-Turbo untuk transkrip lokal..."
                : $"{subtitleProblem ?? "Subtitle YouTube tidak tersedia."} Melanjutkan dengan Whisper Large-v3-Turbo...";
            progress.Report(JobProgress.Stage(message));
            cues = await _whisper.TranscribeAsync(source, language, job, ct);
        }

        if (useSubtitles && !useNarration && !hasTranscriptCache)
        {
            try { _cache.StoreTranscript(transcriptCache, cues); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { progress.Report(JobProgress.Stage("Cache transkrip gagal disimpan; render tetap dilanjutkan.")); }
        }

        string? assFile = null;
        if (useSubtitles)
        {
            assFile = Path.Combine(job, "caption.ass");
            CaptionWriter.WriteAss(assFile, cues, style);
        }

        string? cropExpression = null;
        IReadOnlyList<SmartCropSegment> fullFrameSegments = [];
        if (useFourByFiveContentFrame && !clip.PreserveFullFrame)
        {
            progress.Report(JobProgress.Stage("Menganalisis wajah untuk crop utama 4:5..."));
            var analysis = await AnalyzeFramingCachedAsync(source, job, ct, cropHeight: 1350);
            cropExpression = analysis.CropExpression;
            fullFrameSegments = analysis.FullFrameSegments;
            progress.Report(JobProgress.Stage(!analysis.IsAvailable
                ? "Analisis wajah tidak tersedia; area 4:5 memakai crop tengah."
                : !analysis.HasDetectedFace
                    ? "Wajah tidak terdeteksi; gambar penuh akan tampil di area 4:5 dengan latar blur."
                    : "Wajah utama terlacak di area crop 4:5."));
        }
        else if (!preserveSourceAspectRatio && !useFourByFiveContentFrame && !clip.PreserveFullFrame)
        {
            progress.Report(JobProgress.Stage("Menganalisis wajah untuk smart crop..."));
            var analysis = await AnalyzeFramingCachedAsync(source, job, ct, cropHeight: 1920);
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

        progress.Report(JobProgress.Stage(preserveSourceAspectRatio
            ? "Menyimpan rasio asli video..."
            : "Membuat video 9:16..."));
        try
        {
            await _renderer.RenderAsync(source, assFile, output, job, cropExpression, clip.PreserveFullFrame,
                fullFrameSegments, framePngPath, style.FontFilePath, preserveSourceAspectRatio,
                useFourByFiveContentFrame, narrationTracks, ttsOptions, ct);
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

    private async Task<SmartCropAnalysis> AnalyzeFramingCachedAsync(
        string source, string job, CancellationToken ct, int cropHeight)
    {
        var cachePath = _cache.GetFramingPath(source, cropHeight);
        if (_cache.TryGetFraming(cachePath, out var cached)) return cached;
        var analysis = await new SmartCropAnalyzer().AnalyzeAsync(source, job, ct, cropHeight);
        try { _cache.StoreFraming(cachePath, analysis); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return analysis;
    }

    private async Task<string> DownloadAndJoinSegmentsAsync(
        string url, IReadOnlyList<CondensedSegment> segments, string job,
        IProgress<JobProgress> progress, CancellationToken ct)
    {
        var joinedCachePath = _cache.GetJoinedSegmentsPath(url, segments);
        if (_cache.TryGetVideo(joinedCachePath))
        {
            progress.Report(JobProgress.Stage("Cache rangkaian potongan cocok; melewati unduhan dan penyambungan."));
            return joinedCachePath;
        }

        var partPaths = new List<string>(segments.Count);
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var cachedPart = _cache.GetVideoPath(url, segment.StartSeconds, segment.EndSeconds);
            if (_cache.TryGetVideo(cachedPart))
            {
                progress.Report(JobProgress.Stage($"Cache bagian {i + 1}/{segments.Count} cocok; melewati unduhan."));
                partPaths.Add(cachedPart);
                continue;
            }

            progress.Report(JobProgress.Stage($"Mengunduh bagian {i + 1}/{segments.Count} ({segment.TimeRange})..."));
            var path = Path.Combine(job, $"part-{i + 1:000}.mp4");
            await _ytDlp.DownloadSectionAsync(url, segment.StartSeconds, segment.EndSeconds,
                path, job, p => progress.Report(p with
                {
                    Message = $"Unduh bagian {i + 1}/{segments.Count}",
                    Percent = p.Percent is { } percent ? (i + percent / 100d) / segments.Count * 100 : null
                }), ct);
            try
            {
                _cache.StoreVideo(path, cachedPart);
                partPaths.Add(cachedPart);
                progress.Report(JobProgress.Stage($"Bagian {i + 1}/{segments.Count} disimpan ke cache."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                partPaths.Add(path);
                progress.Report(JobProgress.Stage($"Cache bagian {i + 1} gagal disimpan; render tetap dilanjutkan."));
            }
        }

        var concatList = Path.Combine(job, "kept-segments.ffconcat");
        var lines = new List<string> { "ffconcat version 1.0" };
        lines.AddRange(partPaths.Select(path => "file '" + Path.GetFullPath(path).Replace("\\", "/").Replace("'", "'\\''") + "'"));
        await File.WriteAllLinesAsync(concatList, lines, new System.Text.UTF8Encoding(false), ct);

        var joined = Path.Combine(job, "video-ringkas-source.mp4");
        progress.Report(JobProgress.Stage($"Menyambung {segments.Count} bagian sesuai urutan asli tanpa mengubah kecepatan..."));
        var result = await ProcessRunner.RunAsync(ProcessRunner.RequireTool("ffmpeg"),
            ["-y", "-f", "concat", "-safe", "0", "-i", concatList, "-c", "copy", "-movflags", "+faststart", joined],
            job, line =>
            {
                if (!string.IsNullOrWhiteSpace(line)) progress.Report(JobProgress.Stage(line.Trim()));
            }, ct);
        if (result.ExitCode != 0 || !File.Exists(joined) || new FileInfo(joined).Length < 20_000)
            throw new InvalidOperationException("Bagian video gagal disambung. " + (result.Tail() ?? "File hasil gabungan kosong."));
        try
        {
            _cache.StoreVideo(joined, joinedCachePath);
            progress.Report(JobProgress.Stage("Video gabungan disimpan ke cache; edit subtitle/narasi tidak perlu mengunduh ulang."));
            return joinedCachePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            progress.Report(JobProgress.Stage("Cache gabungan gagal disimpan; render tetap dilanjutkan."));
        }
        return joined;
    }

    /// <summary>Nama unik per video + klip + rentang waktu, supaya scan video lain tidak menimpa hasil sebelumnya.</summary>
    private static string BuildFileName(string title, ClipCandidate clip)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var slug = new string(title.Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '_' : c).ToArray());
        slug = Regex.Replace(slug, "_+", "_").Trim('_', '.');
        if (slug.Length > 40) slug = slug[..40].TrimEnd('_', '.');
        if (slug.Length == 0) slug = "video";
        var prefix = clip.KeptSegments.Count > 0 ? "ringkas_" : string.Empty;
        return $"{slug}_{prefix}{clip.Name}_{TimeSpan.FromSeconds(clip.Start):hhmmss}-{TimeSpan.FromSeconds(clip.End):hhmmss}";
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
