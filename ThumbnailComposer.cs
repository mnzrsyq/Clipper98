using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfFlowDirection = System.Windows.FlowDirection;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;
using HookPath = System.Windows.Shapes.Path;

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

    public static void WriteHookOverlayPng(string outputPath, string rawHook, ThumbnailHookStyle style)
    {
        var (line1, line2) = SplitHookWords(rawHook);
        var hasLine2 = !string.IsNullOrWhiteSpace(line2);
        var canvas = new Canvas { Width = 1080, Height = 1920, ClipToBounds = false };
        var familyName = string.IsNullOrWhiteSpace(style.FontName) ? "Anton" : style.FontName.Trim();
        var family = !string.IsNullOrWhiteSpace(style.FontFilePath) && File.Exists(style.FontFilePath)
            ? SubtitleFonts.ResolvePreviewFamily(new SubtitleFontOption { FamilyName = familyName, FilePath = style.FontFilePath })
            : new WpfFontFamily(familyName);
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Black, FontStretches.Normal);
        var size = Math.Clamp(style.FontSize, 48, 220);
        var first = MakeHookText(line1, typeface, hasLine2 ? size * 0.72 : size, ParseColor(style.FirstLineColor, Colors.White));
        (FormattedText Layout, Geometry Geometry, WpfBrush Fill)? second = hasLine2
            ? MakeHookText(line2, typeface, size, ParseColor(style.SecondLineColor, Colors.Yellow))
            : null;

        var lineSpacing = Math.Clamp(style.LineSpacing, 0, 100);
        var totalHeight = first.Layout.Height + (second is null ? 0 : lineSpacing + second.Value.Layout.Height);
        var top = Math.Clamp(style.PositionY, 150, 1770) - totalHeight / 2;
        AddHookLine(canvas, first, top, style, isFirstLine: true);
        if (second is not null)
            AddHookLine(canvas, second.Value, top + first.Layout.Height + lineSpacing, style, isFirstLine: false);

        canvas.Measure(new WpfSize(1080, 1920));
        canvas.Arrange(new Rect(0, 0, 1080, 1920));
        canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1080, 1920, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        bitmap.Freeze();
        using var stream = File.Create(outputPath);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static (FormattedText Layout, Geometry Geometry, WpfBrush Fill) MakeHookText(
        string text, Typeface typeface, double size, WpfColor color)
    {
        var layout = new FormattedText(text, Inv, WpfFlowDirection.LeftToRight, typeface, size,
            new SolidColorBrush(color), 1.0)
        {
            MaxTextWidth = 960,
            Trimming = TextTrimming.CharacterEllipsis
        };
        return (layout, layout.BuildGeometry(new WpfPoint(0, 0)), new SolidColorBrush(color));
    }

    private static void AddHookLine(
        Canvas canvas, (FormattedText Layout, Geometry Geometry, WpfBrush Fill) text,
        double top, ThumbnailHookStyle style, bool isFirstLine)
    {
        var bounds = text.Geometry.Bounds;
        var left = (1080 - bounds.Width) / 2 - bounds.X;
        var glyphTop = top - bounds.Y;
        var outline = Math.Clamp(style.OutlineSize, 0, 40);

        if (isFirstLine && style.BadgeEnabled && !style.BadgeShape.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            const double paddingX = 28;
            const double paddingY = 14;
            var badgeWidth = Math.Min(1024, text.Layout.WidthIncludingTrailingWhitespace + paddingX * 2);
            var badgeHeight = text.Layout.Height + paddingY * 2;
            var radius = style.BadgeShape.ToLowerInvariant() switch
            {
                "rounded" => 14,
                "pill" => badgeHeight / 2,
                _ => 0
            };
            var badge = new Border
            {
                Width = badgeWidth,
                Height = badgeHeight,
                CornerRadius = new CornerRadius(radius),
                Background = new SolidColorBrush(ParseColor(style.BadgeColor, Colors.Red)),
                Effect = CreateShadow(style)
            };
            Canvas.SetLeft(badge, (1080 - badgeWidth) / 2);
            Canvas.SetTop(badge, top - paddingY);
            canvas.Children.Add(badge);
        }

        var outlineBrush = new SolidColorBrush(ParseColor(style.OutlineColor, Colors.Black));
        var path = new HookPath
        {
            Data = text.Geometry,
            Fill = text.Fill,
            Stroke = outline > 0 ? outlineBrush : null,
            StrokeThickness = outline,
            StrokeLineJoin = PenLineJoin.Round,
            Effect = CreateShadow(style),
            SnapsToDevicePixels = true
        };
        Canvas.SetLeft(path, left);
        Canvas.SetTop(path, glyphTop);
        canvas.Children.Add(path);
    }

    private static DropShadowEffect? CreateShadow(ThumbnailHookStyle style) => !style.ShadowEnabled
        ? null
        : new DropShadowEffect
        {
            Color = ParseColor(style.ShadowColor, Colors.Black),
            BlurRadius = Math.Clamp(style.ShadowBlur, 0, 40),
            ShadowDepth = Math.Clamp(style.ShadowOffset, 0, 30),
            Direction = 315,
            Opacity = Math.Clamp(style.ShadowOpacity, 0, 1)
        };

    private static WpfColor ParseColor(string? value, WpfColor fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try { return (WpfColor)WpfColorConverter.ConvertFromString(value)!; }
        catch (FormatException) { return fallback; }
        catch (InvalidCastException) { return fallback; }
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
        ThumbnailHookStyle hookStyle,
        CancellationToken ct)
    {
        var ffmpeg = ProcessRunner.RequireTool("ffmpeg");
        var hasHook = hookStyle.HookEnabled;
        var overlayPath = Path.Combine(workDir, "thumbnail_hook.png");
        if (hasHook) WriteHookOverlayPng(overlayPath, hookText, hookStyle);

        string baseFilter;
        if (preserveFullFrame)
        {
            baseFilter = "[0:v]split=2[widebg][widefg];[widebg]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=22:10[bg];[widefg]scale=1080:1920:force_original_aspect_ratio=decrease[fg];[bg][fg]overlay=(W-w)/2:(H-h)/2[base]";
        }
        else
        {
            var x = string.IsNullOrWhiteSpace(cropExpression) ? "(in_w-out_w)/2" : cropExpression;
            baseFilter = $"[0:v]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920:x='{x}':y=0[base]";
        }
        var filter = hasHook
            ? $"{baseFilter};[1:v]format=rgba[hook];[base][hook]overlay=0:0:format=auto[out]"
            : $"{baseFilter};[base]null[out]";

        var args = new List<string>
        {
            "-y",
            "-ss", relativeHeroSeconds.ToString("0.###", Inv),
            "-i", sourceVideo
        };
        if (hasHook) args.AddRange(["-i", overlayPath]);
        args.AddRange([
            "-filter_complex", filter,
            "-map", "[out]",
            "-frames:v", "1",
            "-q:v", "2",
            outputJpgPath
        ]);

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
        ThumbnailHookStyle hookStyle,
        double openingDurationSeconds,
        CancellationToken ct)
    {
        var ffmpeg = ProcessRunner.RequireTool("ffmpeg");
        var overlayPath = Path.Combine(workDir, "thumbnail_hook.png");
        WriteHookOverlayPng(overlayPath, hookText, hookStyle);

        string vFilter;
        if (preserveFullFrame)
        {
            vFilter = "[0:v]split=2[widebg][widefg];[widebg]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=22:10[bg];[widefg]scale=1080:1920:force_original_aspect_ratio=decrease[fg];[bg][fg]overlay=(W-w)/2:(H-h)/2,scale=1080:1920[base]";
        }
        else
        {
            var x = string.IsNullOrWhiteSpace(cropExpression) ? "(in_w-out_w)/2" : cropExpression;
            vFilter = $"[0:v]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920:x='{x}':y=0,scale=1080:1920[base]";
        }

        var duration = openingDurationSeconds > 0 ? openingDurationSeconds : 2.0;
        var hasAudio = (await MediaProbe.ProbeAsync(sourceVideo, ct)).HasAudio;
        const string scale = "if(lt(t,0.28),0.3+0.9*t/0.28,if(lt(t,0.42),1.2-(t-0.28)*0.2/0.14,1))";
        var filter = $"{vFilter};[1:v]scale=w='iw*({scale})':h='ih*({scale})':eval=frame[hook];[base][hook]overlay=(W-w)/2:(H-h)/2:shortest=1:format=auto[vout]";

        var args = new List<string>
        {
            "-y",
            "-ss", relativeHeroSeconds.ToString("0.###", Inv),
            "-t", duration.ToString("0.###", Inv),
            "-i", sourceVideo,
            "-loop", "1", "-framerate", "30", "-i", overlayPath,
            "-filter_complex", filter,
            "-map", "[vout]"
        };

        if (hasAudio)
        {
            var fadeStart = Math.Max(0.2, duration - 0.25);
            args.AddRange(["-map", "0:a", "-af", $"afade=t=out:st={fadeStart.ToString("0.###", Inv)}:d=0.25,aresample=48000"]);
        }
        else
        {
            args.AddRange(["-f", "lavfi", "-t", duration.ToString("0.###", Inv), "-i", "anullsrc=channel_layout=stereo:sample_rate=48000", "-map", "2:a"]);
        }

        args.AddRange([
            "-c:v", "h264_amf", "-quality", "speed", "-b:v", "8M", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "192k", "-ar", "48000",
            "-t", duration.ToString("0.###", Inv),
            outputOpeningMp4
        ]);

        var res = await ProcessRunner.RunAsync(ffmpeg, args, workDir, null, ct);
        if (res.ExitCode != 0)
        {
            if (File.Exists(outputOpeningMp4)) File.Delete(outputOpeningMp4);
            var codecIndex = args.IndexOf("h264_amf");
            args[codecIndex] = "libx264";
            args.RemoveRange(codecIndex + 1, 3);
            args.InsertRange(codecIndex + 1, ["-preset", "veryfast", "-crf", "20"]);
            res = await ProcessRunner.RunAsync(ffmpeg, args, workDir, null, ct);
        }
        if (res.ExitCode != 0 || !File.Exists(outputOpeningMp4))
            throw new InvalidOperationException("Gagal merender Opening Video Motion Composite. " + res.Tail());

        return outputOpeningMp4;
    }

    public static async Task ExtractThumbnailFromMotionAsync(
        string motionVideoPath,
        string outputJpgPath,
        string workDir,
        double extractTimeSeconds,
        CancellationToken ct)
    {
        var ffmpeg = ProcessRunner.RequireTool("ffmpeg");
        // Ekstrak pada puncak animasi pop-in (default 1.0s)
        var targetTime = Math.Max(0.5, extractTimeSeconds);
        var args = new List<string>
        {
            "-y",
            "-ss", targetTime.ToString("0.###", Inv),
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
