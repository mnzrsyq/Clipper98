using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Clipper98;

public sealed record ComponentDownloadProgress(string Component, long BytesReceived, long? TotalBytes)
{
    public int? Percent => TotalBytes is > 0 ? (int)Math.Clamp(BytesReceived * 100d / TotalBytes.Value, 0, 100) : null;
    public string Detail => TotalBytes is > 0
        ? $"{FormatBytes(BytesReceived)} / {FormatBytes(TotalBytes.Value)} ({Percent}%)"
        : $"{FormatBytes(BytesReceived)} diterima";

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / 1024d / 1024 / 1024:0.0} GiB"
        : bytes >= 1024L * 1024
            ? $"{bytes / 1024d / 1024:0.0} MiB"
            : $"{bytes / 1024d:0} KiB";
}

public sealed class ComponentDownloadService
{
    private const string ModelName = "ggml-large-v3-turbo.bin";
    private const string FaceModelName = "haarcascade_frontalface_default.xml";
    private static readonly HttpClient Http = CreateClient();
    private static readonly string ToolsDirectory = Path.Combine(AppContext.BaseDirectory, "tools");
    private static readonly string ModelsDirectory = Path.Combine(ToolsDirectory, "models");

    public async Task DownloadMissingAsync(
        IReadOnlyList<string> components,
        IProgress<ComponentDownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        foreach (var component in components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsMissing(component)) continue;
            switch (component)
            {
                case "yt-dlp.exe":
                    await DownloadFileAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe",
                        Path.Combine(ToolsDirectory, "yt-dlp.exe"), component, progress, cancellationToken);
                    break;
                case "Node.js untuk YouTube":
                    await DownloadFileAsync("https://nodejs.org/download/release/latest-v24.x/win-x64/node.exe",
                        Path.Combine(ToolsDirectory, "node.exe"), component, progress, cancellationToken);
                    break;
                case "ffmpeg.exe":
                    await DownloadFfmpegAsync(progress, cancellationToken);
                    break;
                case "whisper-cli.exe":
                    await DownloadWhisperCliAsync(progress, cancellationToken);
                    break;
                case "Whisper Large-v3-Turbo":
                    Directory.CreateDirectory(ModelsDirectory);
                    await DownloadFileAsync(
                        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin?download=true",
                        Path.Combine(ModelsDirectory, ModelName), component, progress, cancellationToken);
                    break;
                case "Model face detection":
                    Directory.CreateDirectory(ModelsDirectory);
                    await DownloadFileAsync(
                        "https://raw.githubusercontent.com/opencv/opencv/4.x/data/haarcascades/haarcascade_frontalface_default.xml",
                        Path.Combine(ModelsDirectory, FaceModelName), component, progress, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Komponen tidak dikenal: {component}");
            }
        }
    }

    private static bool IsMissing(string component) => component switch
    {
        "yt-dlp.exe" => ProcessRunner.FindTool("yt-dlp") is null,
        "Node.js untuk YouTube" => ProcessRunner.FindTool("node") is null && ProcessRunner.FindCommand("node") is null,
        "ffmpeg.exe" => ProcessRunner.FindTool("ffmpeg") is null,
        "whisper-cli.exe" => ProcessRunner.FindTool("whisper-cli") is null,
        "Whisper Large-v3-Turbo" => !File.Exists(Path.Combine(ModelsDirectory, ModelName)),
        "Model face detection" => !File.Exists(Path.Combine(ModelsDirectory, FaceModelName)),
        _ => true
    };

    private static async Task DownloadFfmpegAsync(
        IProgress<ComponentDownloadProgress> progress, CancellationToken cancellationToken)
    {
        var archive = Path.Combine(Path.GetTempPath(), $"clipper-ffmpeg-{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadFileAsync("https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
                archive, "ffmpeg.exe", progress, cancellationToken);
            using var zip = ZipFile.OpenRead(archive);
            var entry = zip.Entries.FirstOrDefault(item =>
                item.FullName.Replace('\\', '/').EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
            if (entry is null) throw new InvalidDataException("Arsip FFmpeg tidak berisi bin/ffmpeg.exe.");
            Directory.CreateDirectory(ToolsDirectory);
            await using var input = entry.Open();
            await using var output = new FileStream(Path.Combine(ToolsDirectory, "ffmpeg.exe"), FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await input.CopyToAsync(output, cancellationToken);
        }
        finally { TryDelete(archive); }
    }

    private static async Task DownloadWhisperCliAsync(
        IProgress<ComponentDownloadProgress> progress, CancellationToken cancellationToken)
    {
        var archiveUrl = await GetLatestWhisperArchiveUrlAsync(cancellationToken);
        var archive = Path.Combine(Path.GetTempPath(), $"clipper-whisper-{Guid.NewGuid():N}.zip");
        var extraction = Path.Combine(Path.GetTempPath(), $"clipper-whisper-{Guid.NewGuid():N}");
        try
        {
            await DownloadFileAsync(archiveUrl, archive, "whisper-cli.exe", progress, cancellationToken);
            using var zip = ZipFile.OpenRead(archive);
            var executable = zip.Entries.FirstOrDefault(item =>
                item.FullName.Replace('\\', '/').EndsWith("/whisper-cli.exe", StringComparison.OrdinalIgnoreCase)
                || item.FullName.Equals("whisper-cli.exe", StringComparison.OrdinalIgnoreCase));
            if (executable is null) throw new InvalidDataException("Arsip whisper.cpp tidak berisi whisper-cli.exe.");

            Directory.CreateDirectory(extraction);
            var prefix = executable.FullName.Replace('\\', '/');
            prefix = prefix[..(prefix.LastIndexOf('/') + 1)];
            foreach (var entry in zip.Entries.Where(item =>
                         item.FullName.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                         && !item.FullName.EndsWith('/')))
            {
                var relative = entry.FullName.Replace('\\', '/')[prefix.Length..];
                if (relative.Contains('/')) continue;
                var destination = Path.Combine(extraction, relative);
                await using var input = entry.Open();
                await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await input.CopyToAsync(output, cancellationToken);
            }

            Directory.CreateDirectory(ToolsDirectory);
            foreach (var file in Directory.EnumerateFiles(extraction))
                File.Move(file, Path.Combine(ToolsDirectory, Path.GetFileName(file)), overwrite: true);
            if (!File.Exists(Path.Combine(ToolsDirectory, "whisper-cli.exe")))
                throw new InvalidDataException("whisper-cli.exe gagal diekstrak.");
        }
        finally
        {
            TryDelete(archive);
            TryDeleteDirectory(extraction);
        }
    }

    private static async Task<string> GetLatestWhisperArchiveUrlAsync(CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(
            "https://api.github.com/repos/ggml-org/whisper.cpp/releases?per_page=30",
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            if (!release.TryGetProperty("assets", out var assets)) continue;
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var name)
                    && name.GetString()?.Equals("whisper-bin-x64.zip", StringComparison.OrdinalIgnoreCase) == true
                    && asset.TryGetProperty("browser_download_url", out var url))
                    return url.GetString() ?? throw new InvalidDataException("URL arsip whisper.cpp kosong.");
            }
        }
        throw new InvalidOperationException("Rilis whisper.cpp tidak menyediakan arsip Windows x64.");
    }

    private static async Task DownloadFileAsync(
        string url,
        string destination,
        string component,
        IProgress<ComponentDownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".download";
        TryDelete(temporary);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            long received = 0;
            {
                await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, useAsync: true);
                var buffer = new byte[131072];
                long nextUnknownLengthReport = 4L * 1024 * 1024;
                int? lastPercent = null;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    received += count;
                    int? currentPercent = length is > 0 ? (int)Math.Clamp(received * 100d / length.Value, 0, 100) : null;
                    if (currentPercent is { } percent && percent != lastPercent)
                    {
                        progress.Report(new ComponentDownloadProgress(component, received, length));
                        lastPercent = percent;
                    }
                    else if (currentPercent is null && received >= nextUnknownLengthReport)
                    {
                        progress.Report(new ComponentDownloadProgress(component, received, length));
                        nextUnknownLengthReport = received + 4L * 1024 * 1024;
                    }
                }
                await output.FlushAsync(cancellationToken);
            }
            if (received == 0) throw new InvalidDataException($"Unduhan {component} kosong.");
            File.Move(temporary, destination, overwrite: true);
            progress.Report(new ComponentDownloadProgress(component, received, length));
        }
        finally { TryDelete(temporary); }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Clipper98", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
