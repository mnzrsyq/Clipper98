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
                progress.Report(JobProgress.Stage("Downloading selected video segments..."));
                source = await _ytDlp.DownloadSectionAsync(
                    url, clip.Start, clip.End, Path.Combine(job, "source.mp4"), job, p => progress.Report(p), ct);
                try
                {
                    _cache.StoreVideo(source, cachedVideo);
                    source = cachedVideo;
                    progress.Report(JobProgress.Stage("Video segments saved to the local cache."));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    progress.Report(JobProgress.Stage("Could not save video segments to the cache. Rendering will continue."));
                }
            }
        }

        var narrationTracks = new List<NarrationTrack>();
        var narrationCues = new List<SubtitleCue>();
        if (useNarration)
        {
            if (clip.KeptSegments.Count == 0)
                throw new InvalidOperationException("Voice-over narration is only available in Condense Video mode.");
            progress.Report(JobProgress.Stage("Preparing narration for the video segments..."));
            var cursor = 0d;
            var previousVoiceEnd = 0d;
            foreach (var segment in clip.KeptSegments)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(segment.Narration))
                    throw new InvalidOperationException("A segment has no narration text. Update the Gemini manifest or turn off narration.");
                var audio = await _tts.GenerateAsync(segment.Narration, ttsOptions, progress, ct);
                var media = await MediaProbe.ProbeAsync(audio, ct);
                if (media.DurationSeconds is not > 0)
                    throw new InvalidOperationException("Could not read the narration audio duration.");
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
            progress.Report(JobProgress.Stage("Subtitles skipped as requested."));
        }
        else if (useNarration)
        {
            cues = narrationCues;
            progress.Report(JobProgress.Stage("Using subtitles from the Gemini narration to match the voice-over."));
        }
        else if (hasTranscriptCache)
        {
            progress.Report(JobProgress.Stage("Transcript cache found. Skipping subtitle download and transcription."));
        }
        else if (!isCondensed && subtitleSource.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            progress.Report(JobProgress.Stage("Looking for YouTube subtitles..."));
            try
            {
                var subtitle = await _ytDlp.DownloadSubtitleAsync(url, language, job, ct);
                if (subtitle is not null)
                {
                    var sourceCues = SubtitleParser.ParseFile(subtitle.Path);
                    cues = SubtitleParser.TrimToRange(sourceCues, clip.Start, clip.End);
                    if (cues.Count > 0)
                    {
                        var sourceType = subtitle.IsAutomatic ? "automatic" : "manual";
                        progress.Report(JobProgress.Stage($"Using {sourceType} YouTube subtitles ({subtitle.Language})."));
                    }
                    else subtitleProblem = "YouTube subtitles contain no text in this clip range.";
                }
                else subtitleProblem = "YouTube subtitles are unavailable in the selected language.";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                subtitleProblem = "Could not retrieve YouTube subtitles: " + ex.Message;
            }
        }

        if (useSubtitles && !useNarration && cues.Count == 0)
        {
            var message = subtitleSource.Equals("whisper", StringComparison.OrdinalIgnoreCase)
                ? "Using Whisper Large-v3-Turbo for local transcription..."
                : $"{subtitleProblem ?? "YouTube subtitles are unavailable."} Continuing with Whisper Large-v3-Turbo...";
            progress.Report(JobProgress.Stage(message));
            cues = await _whisper.TranscribeAsync(source, language, job, ct);
        }

        if (useSubtitles && !useNarration && !hasTranscriptCache)
        {
            try { _cache.StoreTranscript(transcriptCache, cues); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { progress.Report(JobProgress.Stage("Could not save the transcript cache. Rendering will continue.")); }
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
            progress.Report(JobProgress.Stage("Analyzing faces for the main 4:5 crop..."));
            var analysis = await AnalyzeFramingCachedAsync(source, job, ct, cropHeight: 1350);
            cropExpression = analysis.CropExpression;
            fullFrameSegments = analysis.FullFrameSegments;
            progress.Report(JobProgress.Stage(!analysis.IsAvailable
                ? "Face analysis is unavailable. The 4:5 area will use a centered crop."
                : !analysis.HasDetectedFace
                    ? "No face was detected. The full image will appear in the 4:5 area over a blurred background."
                    : "The main face is tracked in the 4:5 crop area."));
        }
        else if (!preserveSourceAspectRatio && !useFourByFiveContentFrame && !clip.PreserveFullFrame)
        {
            progress.Report(JobProgress.Stage("Analyzing faces for Smart Crop..."));
            var analysis = await AnalyzeFramingCachedAsync(source, job, ct, cropHeight: 1920);
            cropExpression = analysis.CropExpression;
            fullFrameSegments = analysis.FullFrameSegments;
            var fallbackSummary = fullFrameSegments.Count == 0
                ? string.Empty
                : $" {fullFrameSegments.Count} segments without a detected face will show the full video over a blurred background.";
            progress.Report(JobProgress.Stage(!analysis.IsAvailable
                ? "Face analysis is unavailable. Using a centered crop as a fallback."
                : !analysis.HasDetectedFace
                    ? "No face was detected. The full clip will appear over a blurred background."
                    : $"The main face is tracked. Smart Crop is active.{fallbackSummary}"));
        }

        progress.Report(JobProgress.Stage(preserveSourceAspectRatio
            ? "Preserving the source aspect ratio..."
            : "Rendering 9:16 video..."));

        var thumbnailPath = Path.ChangeExtension(output, ".jpg");
        var heroSec = clip.HeroFrameSeconds ?? clip.Start;
        var relativeHero = ThumbnailComposer.CalculateRelativeHeroTime(clip, heroSec);
        var hookText = !string.IsNullOrWhiteSpace(clip.ThumbnailHook)
            ? clip.ThumbnailHook
            : (!string.IsNullOrWhiteSpace(clip.Hook) ? clip.Hook : "KLIP TERBAIK");

        var toolsFontsDir = Path.Combine(AppContext.BaseDirectory, "tools", "fonts");
        if (!Directory.Exists(toolsFontsDir))
        {
            var alt = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "fonts");
            if (Directory.Exists(alt)) toolsFontsDir = alt;
        }

        var renderFontsDir = Path.Combine(job, "render-fonts");
        Directory.CreateDirectory(renderFontsDir);
        if (Directory.Exists(toolsFontsDir))
        {
            foreach (var f in Directory.EnumerateFiles(toolsFontsDir))
            {
                var ext = Path.GetExtension(f);
                if (ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase) || ext.Equals(".otf", StringComparison.OrdinalIgnoreCase))
                {
                    var dest = Path.Combine(renderFontsDir, Path.GetFileName(f));
                    if (!File.Exists(dest)) File.Copy(f, dest, overwrite: true);
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(style.FontFilePath) && File.Exists(style.FontFilePath))
        {
            var dest = Path.Combine(renderFontsDir, Path.GetFileName(style.FontFilePath));
            if (!File.Exists(dest)) File.Copy(style.FontFilePath, dest, overwrite: true);
        }
        var fontsDir = renderFontsDir;
        var thumbFont = !string.IsNullOrWhiteSpace(clip.ThumbnailFontName) ? clip.ThumbnailFontName : "Anton";
        var openingDuration = clip.OpeningMotionDurationSeconds > 0 ? clip.OpeningMotionDurationSeconds : 2.0;

        try
        {
            if (clip.EnableOpeningMotion && !preserveSourceAspectRatio)
            {
                progress.Report(JobProgress.Stage($"Rendering Opening Video Motion Composite ({openingDuration:0.#}s)..."));
                var openingMp4 = Path.Combine(job, "opening_motion.mp4");
                await ThumbnailComposer.RenderOpeningMotionCompositeAsync(
                    source, relativeHero, hookText, openingMp4, job, cropExpression,
                    clip.PreserveFullFrame, thumbFont, fontsDir, openingDuration, ct);

                progress.Report(JobProgress.Stage("Mengekstrak cover thumbnail statis dari puncak animasi Video Motion Composite..."));
                await ThumbnailComposer.ExtractThumbnailFromMotionAsync(openingMp4, thumbnailPath, job, Math.Min(openingDuration * 0.6, 1.2), ct);

                var mainRenderedMp4 = Path.Combine(job, "main_rendered.mp4");
                await _renderer.RenderAsync(source, assFile, mainRenderedMp4, job, cropExpression, clip.PreserveFullFrame,
                    fullFrameSegments, framePngPath, style.FontFilePath, preserveSourceAspectRatio,
                    useFourByFiveContentFrame, narrationTracks, ttsOptions, ct);

                progress.Report(JobProgress.Stage("Menyatukan Opening Motion Composite ke awal video klip..."));
                await ThumbnailComposer.PrependOpeningToVideoAsync(openingMp4, mainRenderedMp4, output, job, ct);
            }
            else
            {
                await _renderer.RenderAsync(source, assFile, output, job, cropExpression, clip.PreserveFullFrame,
                    fullFrameSegments, framePngPath, style.FontFilePath, preserveSourceAspectRatio,
                    useFourByFiveContentFrame, narrationTracks, ttsOptions, ct);

                progress.Report(JobProgress.Stage("Membuat cover thumbnail 9:16 statis dengan teks hook..."));
                await ThumbnailComposer.GenerateStaticThumbnailAsync(
                    source, relativeHero, hookText, thumbnailPath, job, cropExpression,
                    clip.PreserveFullFrame, thumbFont, fontsDir, ct);
            }

            progress.Report(JobProgress.Stage("Cover thumbnail 9:16 siap: " + Path.GetFileName(thumbnailPath)));
        }
        catch
        {
            TryDeleteFile(output); // jangan tinggalkan mp4 setengah jadi
            TryDeleteFile(thumbnailPath);
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
                progress.Report(JobProgress.Stage($"Segment cache found for {i + 1}/{segments.Count}. Skipping download."));
                partPaths.Add(cachedPart);
                continue;
            }

            progress.Report(JobProgress.Stage($"Downloading segment {i + 1}/{segments.Count} ({segment.TimeRange})..."));
            var path = Path.Combine(job, $"part-{i + 1:000}.mp4");
            await _ytDlp.DownloadSectionAsync(url, segment.StartSeconds, segment.EndSeconds,
                path, job, p => progress.Report(p with
                {
                    Message = $"Downloading segment {i + 1}/{segments.Count}",
                    Percent = p.Percent is { } percent ? (i + percent / 100d) / segments.Count * 100 : null
                }), ct);
            try
            {
                _cache.StoreVideo(path, cachedPart);
                partPaths.Add(cachedPart);
                progress.Report(JobProgress.Stage($"Segment {i + 1}/{segments.Count} saved to the cache."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                partPaths.Add(path);
                progress.Report(JobProgress.Stage($"Could not cache segment {i + 1}. Rendering will continue."));
            }
        }

        var concatList = Path.Combine(job, "kept-segments.ffconcat");
        var lines = new List<string> { "ffconcat version 1.0" };
        lines.AddRange(partPaths.Select(path => "file '" + Path.GetFullPath(path).Replace("\\", "/").Replace("'", "'\\''") + "'"));
        await File.WriteAllLinesAsync(concatList, lines, new System.Text.UTF8Encoding(false), ct);

        var joined = Path.Combine(job, "video-ringkas-source.mp4");
        progress.Report(JobProgress.Stage($"Joining {segments.Count} segments in source order without changing playback speed..."));
        var result = await ProcessRunner.RunAsync(ProcessRunner.RequireTool("ffmpeg"),
            ["-y", "-f", "concat", "-safe", "0", "-i", concatList, "-c", "copy", "-movflags", "+faststart", joined],
            job, line =>
            {
                if (!string.IsNullOrWhiteSpace(line)) progress.Report(JobProgress.Stage(line.Trim()));
            }, ct);
        if (result.ExitCode != 0 || !File.Exists(joined) || new FileInfo(joined).Length < 20_000)
            throw new InvalidOperationException("Could not join the video segments. " + (result.Tail() ?? "The joined output file is empty."));
        try
        {
            _cache.StoreVideo(joined, joinedCachePath);
            progress.Report(JobProgress.Stage("Joined video saved to the cache. Subtitle and narration edits will not require another download."));
            return joinedCachePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            progress.Report(JobProgress.Stage("Could not cache the joined video. Rendering will continue."));
        }
        return joined;
    }

    /// <summary>Nama unik per video + klip + rentang waktu, supaya scan video lain tidak menimpa hasil sebelumnya.</summary>
    internal static string BuildFileName(string title, ClipCandidate clip)
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
