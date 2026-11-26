using System.Globalization;
using System.Text.RegularExpressions;

namespace Clipper98;

public sealed record MediaInfo(double? DurationSeconds, bool HasAudio);

public static class MediaProbe
{
    private static readonly Regex DurationPattern = new(@"Duration:\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex AudioPattern = new(@"Stream #\d+:\d+(?:\[[^]]+\])?(?:\([^)]*\))?: Audio:", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static async Task<MediaInfo> ProbeAsync(string path, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(ProcessRunner.RequireTool("ffmpeg"),
            ["-hide_banner", "-i", path, "-map", "0:a:0", "-t", "0.02", "-f", "null", "NUL"],
            Path.GetDirectoryName(path), null, ct);
        var text = result.Error + Environment.NewLine + result.Output;
        var duration = DurationPattern.Match(text);
        double? seconds = null;
        if (duration.Success
            && double.TryParse(duration.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var sec))
            seconds = int.Parse(duration.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
                + int.Parse(duration.Groups[2].Value, CultureInfo.InvariantCulture) * 60 + sec;
        return new MediaInfo(seconds, AudioPattern.IsMatch(text));
    }
}
