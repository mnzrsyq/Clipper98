using System.Globalization;
using System.Text;

namespace Clipper98;

public static class ThumbnailComposer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static (string Line1, string Line2) SplitHookWords(string rawHook)
    {
        var clean = (rawHook ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(clean))
            return ("KLIP POPULER", "TONTON SAMPAI HABIS");

        var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 1)
            return (words[0], string.Empty);
        if (words.Length == 2)
            return (words[0], words[1]);
        if (words.Length == 3)
            return (words[0], $"{words[1]} {words[2]}");
        
        // 4 kata atau lebih: pisahkan 2 kata di baris 1, sisanya di baris 2
        var l1 = $"{words[0]} {words[1]}";
        var l2 = string.Join(" ", words.Skip(2));
        return (l1, l2);
    }

    public static void WriteHookAss(string assPath, string rawHook, bool isMotionAnimated, string? fontName = null)
    {
        var (line1, line2) = SplitHookWords(rawHook);
        var resolvedFont = string.IsNullOrWhiteSpace(fontName) ? "Segoe UI Black" : fontName.Trim();

        using var writer = new StreamWriter(assPath, false, new UTF8Encoding(false));
        writer.WriteLine("[Script Info]");
        writer.WriteLine("ScriptType: v4.00+");
        writer.WriteLine("PlayResX: 1080");
        writer.WriteLine("PlayResY: 1920");
        writer.WriteLine();
        writer.WriteLine("[V4+ Styles]");
        writer.WriteLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        // Style Pill: kotak latar belakang berwarna ungu/crimson tua dengan teks putih
        writer.WriteLine($"Style: HookPill,{resolvedFont},66,&H00FFFFFF,&H00000000,&H007A107A,&H007A107A,-1,0,0,0,100,100,3,0,3,18,0,5,50,50,0,1");
        // Style Punch: teks besar tebal warna neon yellow dengan outline hitam pekat dan drop shadow
        writer.WriteLine($"Style: HookPunch,{resolvedFont},98,&H0020E5FF,&H00000000,&H00000000,&H80000000,-1,0,0,0,100,100,2,0,1,14,7,5,50,50,0,1");
        writer.WriteLine();
        writer.WriteLine("[Events]");
        writer.WriteLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        var hasLine2 = !string.IsNullOrWhiteSpace(line2);
        var y1 = hasLine2 ? 470 : 540;
        var y2 = 610;

        if (isMotionAnimated)
        {
            // Efek pop-in scale bounce modern:
            // Baris 1: t=0ms..220ms (scale 35% -> 112%), t=220ms..320ms (112% -> 100%)
            var anim1 = @"{\pos(540," + y1 + @")\fscx35\fscy35\t(0,220,\fscx112\fscy112)\t(220,320,\fscx100\fscy100)}" + line1;
            writer.WriteLine($"Dialogue: 0,0:00:00.00,0:00:05.00,HookPill,,0,0,0,,{anim1}");

            if (hasLine2)
            {
                // Baris 2: t=100ms..340ms (scale 35% -> 115%), t=340ms..440ms (115% -> 100%)
                var anim2 = @"{\pos(540," + y2 + @")\fscx35\fscy35\t(100,340,\fscx115\fscy115)\t(340,440,\fscx100\fscy100)}" + line2;
                writer.WriteLine($"Dialogue: 1,0:00:00.00,0:00:05.00,HookPunch,,0,0,0,,{anim2}");
            }
        }
        else
        {
            // Statis untuk thumbnail langsung
            writer.WriteLine($"Dialogue: 0,0:00:00.00,0:00:05.00,HookPill,,0,0,0,,{{\\pos(540,{y1})}}{line1}");
            if (hasLine2)
                writer.WriteLine($"Dialogue: 1,0:00:00.00,0:00:05.00,HookPunch,,0,0,0,,{{\\pos(540,{y2})}}{line2}");
        }
    }

    public static double CalculateRelativeHeroTime(ClipCandidate clip, double heroSeconds)
    {
        if (clip.KeptSegments.Count > 0)
        {
            var accum = 0d;
            foreach (var seg in clip.KeptSegments)
            {
                if (heroSeconds >= seg.StartSeconds && heroSeconds <= seg.EndSeconds)
                    return accum + (heroSeconds - seg.StartSeconds);
                accum += seg.DurationSeconds;
            }
            return Math.Min(Math.Max(0, accum * 0.5), Math.Max(0, heroSeconds));
        }

        var rel = heroSeconds - clip.Start;
        var total = Math.Max(0.5, clip.End - clip.Start);
        if (rel < 0 || rel >= total)
            rel = total * 0.5;
        return rel;
    }

    public static async Task GenerateStaticThumbnailAsync(
        string sourceVideo,
        double relativeHeroSeconds,
        string hookText,
        string outputJpgPath,
        string workDir,
        string? cropExpression,
        bool preserveFullFrame,
        string? fontName,
        string? fontsDir,
        CancellationToken ct)
    {
        var ffmpeg = ProcessRunner.RequireTool("ffmpeg");
        var assPath = Path.Combine(workDir, "thumb_hook.ass");
        WriteHookAss(assPath, hookText, isMotionAnimated: false, fontName);

        var escapedAss = EscapeFilterPath(assPath);
        var subFilter = $"subtitles=filename='{escapedAss}'";
        if (!string.IsNullOrWhiteSpace(fontsDir) && Directory.Exists(fontsDir))
            subFilter += $":fontsdir='{EscapeFilterPath(fontsDir)}'";

        string filter;
        if (preserveFullFrame)
        {
            filter = $"split=2[widebg][widefg];[widebg]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=22:10[bg];[widefg]scale=1080:1920:force_original_aspect_ratio=decrease[fg];[bg][fg]overlay=(W-w)/2:(H-h)/2[base];[base]{subFilter}";
        }
        else
        {
            var x = string.IsNullOrWhiteSpace(cropExpression) ? "(in_w-out_w)/2" : cropExpression;
            filter = $"scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920:x='{x}':y=0[base];[base]{subFilter}";
        }

        var args = new List<string>
        {
            "-y",
            "-ss", relativeHeroSeconds.ToString("0.###", Inv),
            "-i", sourceVideo,
            "-vf", filter,
            "-frames:v", "1",
            "-q:v", "2",
            outputJpgPath
        };

        var res = await ProcessRunner.RunAsync(ffmpeg, args, workDir, null, ct);
        if (res.ExitCode != 0 || !File.Exists(outputJpgPath))
            throw new InvalidOperationException("Gagal membuat cover thumbnail statis. " + res.Tail());
    }

    public static async Task<string> RenderOpeningMotionCompositeAsync(
        string sourceVideo,
        double relativeHeroSeconds,
        string hookText,
        string outputOpeningMp4,
        string workDir,
        string? cropExpression,
        bool preserveFullFrame,
        string? fontName,
        string? fontsDir,
        CancellationToken ct)
    {
        var ffmpeg = ProcessRunner.RequireTool("ffmpeg");
        var assPath = Path.Combine(workDir, "motion_hook.ass");
        WriteHookAss(assPath, hookText, isMotionAnimated: true, fontName);

        var escapedAss = EscapeFilterPath(assPath);
        var subFilter = $"subtitles=filename='{escapedAss}'";
        if (!string.IsNullOrWhiteSpace(fontsDir) && Directory.Exists(fontsDir))
            subFilter += $":fontsdir='{EscapeFilterPath(fontsDir)}'";

        string vFilter;
        if (preserveFullFrame)
        {
            vFilter = $"split=2[widebg][widefg];[widebg]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=22:10[bg];[widefg]scale=1080:1920:force_original_aspect_ratio=decrease[fg];[bg][fg]overlay=(W-w)/2:(H-h)/2,scale=1080:1920[base];[base]{subFilter}";
        }
        else
        {
            var x = string.IsNullOrWhiteSpace(cropExpression) ? "(in_w-out_w)/2" : cropExpression;
            vFilter = $"scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920:x='{x}':y=0,scale=1080:1920[base];[base]{subFilter}";
        }

        const double openingDuration = 2.2;
        var hasAudio = (await MediaProbe.ProbeAsync(sourceVideo, ct)).HasAudio;

        var args = new List<string>
        {
            "-y",
            "-ss", relativeHeroSeconds.ToString("0.###", Inv),
            "-t", openingDuration.ToString("0.###", Inv),
            "-i", sourceVideo,
            "-filter_complex", $"[0:v]{vFilter}[vout]",
            "-map", "[vout]"
        };

        if (hasAudio)
        {
            args.AddRange(["-map", "0:a", "-af", "afade=t=out:st=2.0:d=0.2,aresample=48000"]);
        }
        else
        {
            args.AddRange(["-f", "lavfi", "-t", openingDuration.ToString("0.###", Inv), "-i", "anullsrc=channel_layout=stereo:sample_rate=48000", "-map", "1:a"]);
        }

        args.AddRange([
            "-c:v", "libx264", "-preset", "fast", "-crf", "20", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "192k", "-ar", "48000",
            "-t", openingDuration.ToString("0.###", Inv),
            outputOpeningMp4
        ]);

        var res = await ProcessRunner.RunAsync(ffmpeg, args, workDir, null, ct);
        if (res.ExitCode != 0 || !File.Exists(outputOpeningMp4))
            throw new InvalidOperationException("Gagal merender Opening Video Motion Composite. " + res.Tail());

        return outputOpeningMp4;
    }

    public static async Task ExtractThumbnailFromMotionAsync(
        string motionVideoPath,
        string outputJpgPath,
        string workDir,
        CancellationToken ct)
    {
        var ffmpeg = ProcessRunner.RequireTool("ffmpeg");
        // Ekstrak pada detik 1.2s saat animasi pop-in sudah selesai membal dan semua elemen tampak penuh
        var args = new List<string>
        {
            "-y",
            "-ss", "00:00:01.200",
            "-i", motionVideoPath,
            "-frames:v", "1",
            "-q:v", "2",
            outputJpgPath
        };

        var res = await ProcessRunner.RunAsync(ffmpeg, args, workDir, null, ct);
        if (res.ExitCode != 0 || !File.Exists(outputJpgPath))
            throw new InvalidOperationException("Gagal mengekstrak thumbnail dari hasil Video Motion Composite. " + res.Tail());
    }

    public static async Task PrependOpeningToVideoAsync(
        string openingMp4,
        string mainVideoMp4,
        string finalOutputMp4,
        string workDir,
        CancellationToken ct)
    {
        var ffmpeg = ProcessRunner.RequireTool("ffmpeg");
        var concatList = Path.Combine(workDir, "opening_concat.txt");
        var lines = new[]
        {
            "ffconcat version 1.0",
            "file '" + Path.GetFullPath(openingMp4).Replace("\\", "/").Replace("'", "'\\''") + "'",
            "file '" + Path.GetFullPath(mainVideoMp4).Replace("\\", "/").Replace("'", "'\\''") + "'"
        };
        await File.WriteAllLinesAsync(concatList, lines, new UTF8Encoding(false), ct);

        // Coba direct stream copy terlebih dahulu
        var argsCopy = new List<string>
        {
            "-y",
            "-f", "concat",
            "-safe", "0",
            "-i", concatList,
            "-c", "copy",
            "-movflags", "+faststart",
            finalOutputMp4
        };

        var copyRes = await ProcessRunner.RunAsync(ffmpeg, argsCopy, workDir, null, ct);
        if (copyRes.ExitCode == 0 && File.Exists(finalOutputMp4) && new FileInfo(finalOutputMp4).Length > 10_000)
            return;

        // Fallback filter complex concat jika stream parameter berbeda
        var argsReencode = new List<string>
        {
            "-y",
            "-i", openingMp4,
            "-i", mainVideoMp4,
            "-filter_complex", "[0:v][0:a][1:v][1:a]concat=n=2:v=1:a=1[vout][aout]",
            "-map", "[vout]",
            "-map", "[aout]",
            "-c:v", "libx264", "-preset", "fast", "-crf", "20", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "192k",
            "-movflags", "+faststart",
            finalOutputMp4
        };

        var reencodeRes = await ProcessRunner.RunAsync(ffmpeg, argsReencode, workDir, null, ct);
        if (reencodeRes.ExitCode != 0 || !File.Exists(finalOutputMp4))
            throw new InvalidOperationException("Gagal menggabungkan Opening Motion dengan video utama. " + reencodeRes.Tail());
    }

    private static string EscapeFilterPath(string path) =>
        path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
}
