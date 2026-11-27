using System.Text;
using System.Text.Json;

namespace Clipper98;

public sealed record VideoMetadata(string Title, string Description, bool Found);

public static class VideoMetadataCodec
{
    private const string CommentPrefix = "clipper98_metadata_v1:";

    public static string EncodeComment(string title, string description)
    {
        var json = JsonSerializer.Serialize(new VideoMetadataPayload(title, description));
        return CommentPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static async Task WriteAsync(string videoPath, string title, string description, string workDir, CancellationToken ct)
    {
        var ffmpeg = ProcessRunner.RequireTool("ffmpeg");
        var tempPath = Path.Combine(workDir, "metadata-" + Guid.NewGuid().ToString("N") + ".mp4");
        var result = await ProcessRunner.RunAsync(ffmpeg,
        [
            "-y", "-i", videoPath, "-map", "0", "-c", "copy",
            "-metadata", $"title={title}", "-metadata", $"description={description}",
            "-metadata", $"comment={EncodeComment(title, description)}", "-movflags", "+faststart", tempPath
        ], workDir, null, ct);
        if (result.ExitCode != 0 || !File.Exists(tempPath))
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw new InvalidOperationException("Could not write upload metadata into the rendered MP4. " + result.Tail());
        }
        File.Move(tempPath, videoPath, overwrite: true);
    }

    public static async Task<VideoMetadata> ReadAsync(string videoPath, CancellationToken ct = default)
    {
        var ffmpeg = ProcessRunner.FindTool("ffmpeg");
        if (ffmpeg is null) return new VideoMetadata(string.Empty, string.Empty, false);
        var result = await ProcessRunner.RunAsync(ffmpeg,
            ["-v", "error", "-i", videoPath, "-map_metadata", "0", "-f", "ffmetadata", "pipe:1"],
            Path.GetDirectoryName(videoPath), null, ct);
        if (result.ExitCode != 0) return new VideoMetadata(string.Empty, string.Empty, false);
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            tags[line[..separator]] = Unescape(line[(separator + 1)..]);
        }
        var title = tags.GetValueOrDefault("title", string.Empty);
        var description = tags.GetValueOrDefault("description", string.Empty);
        var comment = tags.GetValueOrDefault("comment", string.Empty);
        if (comment.StartsWith(CommentPrefix, StringComparison.Ordinal))
        {
            try
            {
                var payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(comment[CommentPrefix.Length..]));
                var payload = JsonSerializer.Deserialize<VideoMetadataPayload>(payloadJson);
                if (payload is not null)
                    return new VideoMetadata(payload.Title, payload.Description, true);
            }
            catch (Exception ex) when (ex is FormatException or JsonException) { }
        }
        return new VideoMetadata(title, description, !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(description));
    }

    private static string Unescape(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length) { builder.Append(value[i]); continue; }
            var next = value[++i];
            builder.Append(next == 'n' ? '\n' : next);
        }
        return builder.ToString();
    }

    private sealed record VideoMetadataPayload(string Title, string Description);
}
