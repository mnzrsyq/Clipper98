using System.Globalization;

namespace Clipper98;

/// <summary>Render 9:16, optional smart crop or full-frame fill, PNG frame, and burned-in captions.</summary>
public sealed class ClipRenderer
{
    private const string Preset = "fast";
    private const int Crf = 20;

    public async Task RenderAsync(
        string source, string assFile, string output, string workDir,
        string? cropExpression, bool preserveFullFrame, IReadOnlyList<SmartCropSegment>? fullFrameSegments,
        string? framePngPath, string? fontFilePath, CancellationToken ct)
    {
        var ffmpeg = ProcessRunner.RequireTool("ffmpeg");
        var hasFrame = !string.IsNullOrWhiteSpace(framePngPath);
        if (hasFrame && !File.Exists(framePngPath))
            throw new FileNotFoundException("File frame PNG yang dipilih tidak ditemukan.", framePngPath);

        var fontsDir = Path.Combine(AppContext.BaseDirectory, "tools", "fonts");
        if (!string.IsNullOrWhiteSpace(fontFilePath))
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
        var escapedAss = EscapeFilterPath(assFile);
        var subtitleFilter = $"subtitles=filename='{escapedAss}'";
        if (Directory.Exists(fontsDir)) subtitleFilter += $":fontsdir='{EscapeFilterPath(fontsDir)}'";

        var graph = new List<string>();
        if (preserveFullFrame)
        {
            graph.Add("[0:v]split=2[widebg][widefg]");
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
                graph.Add($"[0:v]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920:x='{x}':y=0[base]");
            }
            else
            {
                var enable = string.Join("+", intervals.Select(segment =>
                    $"between(t,{F(segment.Start)},{F(segment.End)})"));
                graph.Add("[0:v]split=3[cropin][widebg][widefg]");
                graph.Add($"[cropin]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920:x='{x}':y=0[cropbase]");
                graph.Add("[widebg]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=22:10[blurred]");
                graph.Add("[widefg]scale=1080:1920:force_original_aspect_ratio=decrease[fit]");
                graph.Add($"[cropbase][blurred]overlay=0:0:enable='{enable}'[gapbg]");
                graph.Add($"[gapbg][fit]overlay=(W-w)/2:(H-h)/2:enable='{enable}'[base]");
            }
        }

        var captionInput = "base";
        if (hasFrame)
        {
            graph.Add("[1:v]scale=1080:1920,format=rgba[frame]");
            graph.Add("[base][frame]overlay=0:0:eof_action=repeat[framed]");
            captionInput = "framed";
        }
        graph.Add($"[{captionInput}]{subtitleFilter}[vout]");

        var args = new List<string> { "-y", "-i", source };
        if (hasFrame) args.AddRange(["-loop", "1", "-i", framePngPath!]);
        args.AddRange([
            "-filter_complex", string.Join(";", graph),
            "-map", "[vout]", "-map", "0:a?", "-shortest",
            "-c:v", "libx264", "-preset", Preset, "-crf", Crf.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", output
        ]);

        var result = await ProcessRunner.RunAsync(ffmpeg, args, workDir, null, ct);
        if (result.ExitCode != 0) throw new InvalidOperationException("Render video gagal. " + result.Tail());
    }

    private static string EscapeFilterPath(string path) =>
        path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
