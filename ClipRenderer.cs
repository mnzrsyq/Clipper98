using System.Globalization;

namespace Clipper98;

/// <summary>Render either 9:16 or source aspect ratio, with optional framing, PNG frame, and captions.</summary>
public sealed class ClipRenderer
{
    private const string Preset = "ultrafast";
    private const int Crf = 20;

    public async Task RenderAsync(
        string source, string? assFile, string output, string workDir,
        string? cropExpression, bool preserveFullFrame, IReadOnlyList<SmartCropSegment>? fullFrameSegments,
        string? framePngPath, string? fontFilePath, bool preserveSourceAspectRatio,
        bool useFourByFiveContentFrame, IReadOnlyList<NarrationTrack> narrationTracks,
        TtsOptions ttsOptions, CancellationToken ct, string metadataTitle = "", string metadataDescription = "",
        IProgress<JobProgress>? progress = null, string metadataDay = "", int metadataSlot = 0)
    {
        var ffmpeg = ProcessRunner.RequireTool("ffmpeg");
        var renderThreads = Math.Max(1, Environment.ProcessorCount);
        var hasFrame = !preserveSourceAspectRatio && !string.IsNullOrWhiteSpace(framePngPath);
        var hasSubtitles = !string.IsNullOrWhiteSpace(assFile);
        var hasNarration = narrationTracks.Count > 0;
        var sourceHasAudio = hasNarration && (await MediaProbe.ProbeAsync(source, ct)).HasAudio;
        if (hasFrame && !File.Exists(framePngPath))
            throw new FileNotFoundException("File frame PNG yang dipilih tidak ditemukan.", framePngPath);

        var fontsDir = Path.Combine(AppContext.BaseDirectory, "tools", "fonts");
        if (hasSubtitles && !string.IsNullOrWhiteSpace(fontFilePath))
        {
            if (!File.Exists(fontFilePath))
                throw new FileNotFoundException("File font yang dipilih tidak ditemukan. Pilih ulang font tersebut.", fontFilePath);
            fontsDir = Path.Combine(workDir, "render-fonts");
            Directory.CreateDirectory(fontsDir);
            var selectedFontDirectory = Path.GetDirectoryName(Path.GetFullPath(fontFilePath))!;
            foreach (var font in Directory.EnumerateFiles(selectedFontDirectory)
                         .Where(path => Path.GetExtension(path).Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                                     || Path.GetExtension(path).Equals(".otf", StringComparison.OrdinalIgnoreCase)))
                File.Copy(font, Path.Combine(fontsDir, Path.GetFileName(font)), overwrite: true);
        }
        var subtitleFilter = string.Empty;
        if (hasSubtitles)
        {
            var escapedAss = EscapeFilterPath(assFile!);
            subtitleFilter = $"subtitles=filename='{escapedAss}'";
            if (Directory.Exists(fontsDir)) subtitleFilter += $":fontsdir='{EscapeFilterPath(fontsDir)}'";
        }

        var graph = new List<string>();
        graph.Add("[0:v]fps=30[rate30]");
        var baseLabel = "base";
        if (preserveSourceAspectRatio)
        {
            graph.Add("[rate30]null[base]");
        }
        else if (useFourByFiveContentFrame)
        {
            var x = string.IsNullOrWhiteSpace(cropExpression)
                ? "(in_w-out_w)/2"
                : cropExpression;
            graph.Add("[rate30]split=3[bgsrc][cropin][fitin]");
            graph.Add("[bgsrc]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=22:10[blurredbg]");
            graph.Add($"[cropin]scale=1080:1350:force_original_aspect_ratio=increase,crop=1080:1350:x='{x}':y='(ih-oh)/2'[content45]");
            graph.Add("[blurredbg][content45]overlay=0:(H-h)/2[base]");

            var intervals = fullFrameSegments?.Where(segment => segment.End > segment.Start).ToList() ?? [];
            if (intervals.Count > 0)
            {
                var enable = string.Join("+", intervals.Select(segment =>
                    $"between(t,{F(segment.Start)},{F(segment.End)})"));
                graph.Add("[fitin]scale=1080:1350:force_original_aspect_ratio=decrease[fullcontent45]");
                graph.Add($"[base][fullcontent45]overlay=0:(H-h)/2:enable='{enable}'[fallbackbase]");
                baseLabel = "fallbackbase";
            }
        }
        else if (preserveFullFrame)
        {
            graph.Add("[rate30]split=2[widebg][widefg]");
            graph.Add("[widebg]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=22:10[bg]");
            graph.Add("[widefg]scale=1080:1920:force_original_aspect_ratio=decrease[fg]");
            graph.Add("[bg][fg]overlay=(W-w)/2:(H-h)/2[base]");
        }
        else
        {
            var x = string.IsNullOrWhiteSpace(cropExpression)
                ? "(in_w-out_w)/2"
                : cropExpression;
            var intervals = fullFrameSegments?.Where(segment => segment.End > segment.Start).ToList() ?? [];
            if (intervals.Count == 0)
            {
                // FFmpeg crop evaluates x/y per frame; this bundled build has no `eval` option.
                graph.Add($"[rate30]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920:x='{x}':y=0[base]");
            }
            else
            {
                var enable = string.Join("+", intervals.Select(segment =>
                    $"between(t,{F(segment.Start)},{F(segment.End)})"));
                graph.Add("[rate30]split=3[cropin][widebg][widefg]");
                graph.Add($"[cropin]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920:x='{x}':y=0[cropbase]");
                graph.Add("[widebg]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=22:10[blurred]");
                graph.Add("[widefg]scale=1080:1920:force_original_aspect_ratio=decrease[fit]");
                graph.Add($"[cropbase][blurred]overlay=0:0:enable='{enable}'[gapbg]");
                graph.Add($"[gapbg][fit]overlay=(W-w)/2:(H-h)/2:enable='{enable}'[base]");
            }
        }

        var captionInput = baseLabel;
        if (hasFrame)
        {
            graph.Add("[1:v]scale=1080:1920,format=rgba[frame]");
            graph.Add("[base][frame]overlay=0:0:eof_action=repeat[framed]");
            captionInput = "framed";
        }
        graph.Add(hasSubtitles ? $"[{captionInput}]{subtitleFilter}[vout]" : $"[{captionInput}]null[vout]");

        if (hasNarration)
        {
            var firstNarrationInput = 1 + (hasFrame ? 1 : 0);
            var pitchFactor = Math.Pow(2, Math.Clamp(ttsOptions.PitchCents, -1200, 1200) / 1200d);
            var tempoFactor = 1 / pitchFactor;
            var voiceInputs = new List<string>();
            for (var i = 0; i < narrationTracks.Count; i++)
            {
                var inputIndex = firstNarrationInput + i;
                var label = $"voice{i}";
                var delayMs = Math.Max(0, (long)Math.Round(narrationTracks[i].StartSeconds * 1000));
                graph.Add($"[{inputIndex}:a]aresample=48000,asetrate=48000*{F(pitchFactor)},aresample=48000,atempo={F(tempoFactor)},volume={F(Math.Clamp(ttsOptions.VolumePercent, 0, 150) / 100d)},adelay={delayMs}|{delayMs}[{label}]");
                voiceInputs.Add($"[{label}]");
            }
            graph.Add($"{string.Join(string.Empty, voiceInputs)}amix=inputs={voiceInputs.Count}:duration=longest:normalize=0[voice_mix]");
            if (sourceHasAudio)
            {
                graph.Add($"[0:a]volume={F(Math.Clamp(ttsOptions.OriginalVolumePercent, 0, 150) / 100d)}[original_mix]");
                graph.Add("[original_mix][voice_mix]amix=inputs=2:duration=first:normalize=0[aout]");
            }
            else graph.Add("[voice_mix]anull[aout]");
        }

        var filterGraph = string.Join(";", graph);
        var args = new List<string>
        {
            "-y", "-threads", renderThreads.ToString(CultureInfo.InvariantCulture), "-i", source
        };
        if (hasFrame) args.AddRange(["-loop", "1", "-i", framePngPath!]);
        foreach (var track in narrationTracks) args.AddRange(["-i", track.FilePath]);
        args.AddRange([
            "-filter_complex_threads", renderThreads.ToString(CultureInfo.InvariantCulture),
            "-filter_complex", filterGraph,
            "-map", "[vout]"
        ]);
        if (hasNarration) args.AddRange(["-map", "[aout]"]);
        else args.AddRange(["-map", "0:a?"]);
        args.AddRange([
            "-shortest", "-metadata", $"title={metadataTitle}", "-metadata", $"description={metadataDescription}",
            "-metadata", $"comment={VideoMetadataCodec.EncodeComment(metadataTitle, metadataDescription, metadataDay, metadataSlot)}",
            "-c:v", "libx264", "-preset", Preset, "-crf", Crf.ToString(CultureInfo.InvariantCulture),
            "-threads", renderThreads.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p", "-r", "30",
            "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", output
        ]);

        void ReportFfmpegProgress(string encoder, string line)
        {
            if (line.Contains("frame=", StringComparison.OrdinalIgnoreCase))
            {
                var summary = string.Join(" ", line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                progress?.Report(JobProgress.Stage($"Render {encoder} 30 fps: {summary}"));
            }
        }

        var amfArgs = args.ToList();
        var codecIndex = amfArgs.IndexOf("libx264");
        amfArgs[codecIndex] = "h264_amf";
        amfArgs.RemoveRange(codecIndex + 1, 6);
        amfArgs.InsertRange(codecIndex + 1, ["-quality", "speed", "-b:v", "8M"]);

        progress?.Report(JobProgress.Stage("Mencoba render 30 fps dengan encoder GPU AMD..."));
        var result = await ProcessRunner.RunAsync(ffmpeg, amfArgs, workDir,
            line => ReportFfmpegProgress("GPU AMD", line), ct);
        if (result.ExitCode != 0)
        {
            if (File.Exists(output)) File.Delete(output);
            var reason = result.Tail(2);
            progress?.Report(JobProgress.Stage(string.IsNullOrWhiteSpace(reason)
                ? $"Encoder GPU AMD gagal; beralih ke CPU ({renderThreads} threads)..."
                : $"Encoder GPU AMD gagal ({reason}); beralih ke CPU ({renderThreads} threads)..."));
            result = await ProcessRunner.RunAsync(ffmpeg, args, workDir,
                line => ReportFfmpegProgress($"CPU {renderThreads} threads", line), ct);
        }
        if (result.ExitCode != 0) throw new InvalidOperationException("Render video gagal. " + result.Tail());
    }

    private static string EscapeFilterPath(string path) =>
        path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
