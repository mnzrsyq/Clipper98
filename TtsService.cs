using System.IO.Compression;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Clipper98;

/// <summary>Downloads on demand, generates cached narration, and keeps runtimes/models in the user's profile.</summary>
public sealed class TtsService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim SetupLock = new(1, 1);
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clipper98", "TTS");
    private static readonly string CacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clipper98", "Cache", "tts");
    private static readonly string UvPath = Path.Combine(Root, "uv", "uv.exe");
    private static readonly string PythonInstall = Path.Combine(Root, "python");
    private const string FishEndpoint = "https://api.fish.audio/v1/tts";
    private const string FishModel = "s2.1-pro-free";
    private const string BudiVoiceId = "5517071fef3e4a31aaa00c8cbf303a82";

    public async Task<string> GenerateAsync(string text, TtsOptions options, IProgress<JobProgress> progress, CancellationToken ct)
    {
        text = text.Trim();
        if (text.Length == 0) throw new InvalidOperationException("Teks narasi kosong.");
        var provider = options.Provider.Equals("edge", StringComparison.OrdinalIgnoreCase) ? "edge" : "fish";
        var keyMaterial = string.Join('|', provider, options.Voice, options.RatePercent, text);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial))).ToLowerInvariant();
        var extension = ".mp3";
        var cached = Path.Combine(CacheRoot, key + extension);
        if (IsValidAudio(cached))
        {
            progress.Report(JobProgress.Stage("Cache narasi cocok; melewati pembuatan suara."));
            return cached;
        }

        Directory.CreateDirectory(CacheRoot);
        var temporaryDir = Path.Combine(Root, "jobs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDir);
        try
        {
            if (provider == "edge")
            {
                var python = await EnsurePythonAsync(progress, ct);
                var env = Path.Combine(Root, "env-edge");
                await EnsureEnvironmentAsync(python, env, "edge-tts", progress, ct);
                var envPython = Path.Combine(env, "Scripts", "python.exe");
                var textFile = Path.Combine(temporaryDir, "narration.txt");
                await File.WriteAllTextAsync(textFile, text, new UTF8Encoding(false), ct);
                var resultFile = Path.Combine(temporaryDir, "narration.mp3");
                progress.Report(JobProgress.Stage("Membuat narasi melalui edge-TTS (butuh koneksi internet)..."));
                await RunAsync(envPython,
                ["-m", "edge_tts", "--file", textFile, "--voice", NormalizeEdgeVoice(options.Voice),
                 $"--rate={FormatEdgeRate(options.RatePercent)}", "--write-media", resultFile], temporaryDir, ct);
                EnsureGeneratedAudio(resultFile);
                File.Move(resultFile, cached, overwrite: true);
            }
            else await GenerateFishAudioAsync(text, options, cached, progress, ct);
            return cached;
        }
        finally { TryDeleteDirectory(temporaryDir); }
    }

    private static async Task<string> EnsurePythonAsync(IProgress<JobProgress> progress, CancellationToken ct)
    {
        await SetupLock.WaitAsync(ct);
        try
        {
            var python = Directory.Exists(PythonInstall)
                ? Directory.EnumerateFiles(PythonInstall, "python.exe", SearchOption.AllDirectories).FirstOrDefault()
                : null;
            if (python is not null) return python;
            Directory.CreateDirectory(Path.GetDirectoryName(UvPath)!);
            if (!File.Exists(UvPath))
            {
                progress.Report(JobProgress.Stage("Mengunduh uv untuk menyiapkan runtime TTS lokal..."));
                var archive = Path.Combine(Path.GetTempPath(), $"clipper-uv-{Guid.NewGuid():N}.zip");
                try
                {
                    await DownloadAsync("https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip", archive, ct);
                    using var zip = ZipFile.OpenRead(archive);
                    var entry = zip.Entries.FirstOrDefault(x => x.FullName.EndsWith("/uv.exe", StringComparison.OrdinalIgnoreCase)
                        || x.FullName.Equals("uv.exe", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException("Unduhan uv tidak berisi uv.exe.");
                    Directory.CreateDirectory(Path.GetDirectoryName(UvPath)!);
                    await using var input = entry.Open();
                    await using var output = new FileStream(UvPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    await input.CopyToAsync(output, ct);
                }
                finally { TryDelete(archive); }
            }
            progress.Report(JobProgress.Stage("Mengunduh Python 3.11 khusus TTS..."));
            await RunAsync(UvPath, ["python", "install", "3.11", "--install-dir", PythonInstall], Root, ct);
            python = Directory.EnumerateFiles(PythonInstall, "python.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidOperationException("Runtime Python untuk TTS tidak ditemukan setelah unduhan.");
            return python;
        }
        finally { SetupLock.Release(); }
    }

    private static async Task EnsureEnvironmentAsync(
        string python, string env, string package, IProgress<JobProgress> progress, CancellationToken ct)
    {
        await SetupLock.WaitAsync(ct);
        try
        {
            var envPython = Path.Combine(env, "Scripts", "python.exe");
            var cli = Path.Combine(env, "Scripts", "edge-tts.exe");
            var marker = Path.Combine(env, ".clipper-ready-" + package);
            if (File.Exists(envPython) && File.Exists(cli) && File.Exists(marker)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(env)!);
            if (!File.Exists(envPython))
            {
                progress.Report(JobProgress.Stage("Menyiapkan lingkungan Python TTS terpisah..."));
                await RunAsync(UvPath, ["venv", "--python", python, env], Root, ct);
            }
            progress.Report(JobProgress.Stage($"Mengunduh dan memasang {package}..."));
            await RunAsync(UvPath, ["pip", "install", "--python", envPython, "--index-url", "https://pypi.org/simple", package], Root, ct);
            File.WriteAllText(marker, "ok");
        }
        finally { SetupLock.Release(); }
    }

    private static async Task GenerateFishAudioAsync(
        string text, TtsOptions options, string destination, IProgress<JobProgress> progress, CancellationToken ct)
    {
        var apiKey = SettingsStore.Load().FishApiKey.Trim();
        if (apiKey.Length == 0)
            throw new InvalidOperationException("API key Fish Audio belum diisi. Buka Pengaturan Suara, pilih Fish Audio, lalu tempel API key.");

        progress.Report(JobProgress.Stage("Membuat narasi melalui Fish Audio S2.1 Pro Free..."));
        var speed = Math.Clamp(1d + options.RatePercent / 100d, 0.5d, 2d);
        var voiceId = options.Voice == "budi" ? BudiVoiceId : options.Voice;
        var payload = JsonSerializer.Serialize(new
        {
            text,
            format = "mp3",
            reference_id = voiceId,
            prosody = new { speed }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, FishEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("model", FishModel);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            if (detail.Length > 1200) detail = detail[..1200];
            var message = response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "API key Fish Audio ditolak (401). Periksa key yang disimpan di pengaturan suara.",
                System.Net.HttpStatusCode.PaymentRequired => "Fish Audio menolak model gratis (402). Pastikan header model S2.1 Pro Free tersedia untuk akun API ini.",
                (System.Net.HttpStatusCode)429 => "Fish Audio sedang membatasi permintaan (429). Coba lagi beberapa saat.",
                _ => $"Fish Audio gagal ({(int)response.StatusCode}): {detail}"
            };
            throw new InvalidOperationException(message);
        }

        await using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            await response.Content.CopyToAsync(output, ct);
        EnsureGeneratedAudio(destination);
    }

    private static string NormalizeEdgeVoice(string voice) => voice is "id-ID-GadisNeural" or "id-ID-ArdiNeural" ? voice : "id-ID-GadisNeural";

    private static string FormatEdgeRate(int ratePercent)
    {
        var rate = Math.Clamp(ratePercent, -50, 100);
        return rate == 0 ? "+0%" : $"{rate:+#;-#}%";
    }

    private static async Task RunAsync(
        string exe, IEnumerable<string> args, string workingDirectory, CancellationToken ct,
        IEnumerable<string>? additionalPath = null)
    {
        var result = await ProcessRunner.RunAsync(exe, args, workingDirectory, null, ct, additionalPath);
        if (result.ExitCode != 0) throw new InvalidOperationException(result.Tail(10));
    }

    private static async Task DownloadAsync(
        string url, string destination, CancellationToken ct, IProgress<JobProgress>? progress = null, string component = "Unduhan TTS")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".download";
        TryDelete(temporary);
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            long received = 0;
            {
                await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
                var buffer = new byte[131072];
                var watch = Stopwatch.StartNew();
                long nextReport = 8L * 1024 * 1024;
                int? lastPercent = null;
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    received += count;
                    int? percent = total is > 0 ? (int)Math.Clamp(received * 100d / total.Value, 0, 100) : null;
                    if ((percent is { } value && value != lastPercent) || (percent is null && received >= nextReport))
                    {
                        var rate = received / Math.Max(watch.Elapsed.TotalSeconds, 0.1);
                        var size = total is > 0
                            ? $"{received / 1024d / 1024:0.0} / {total.Value / 1024d / 1024:0.0} MiB"
                            : $"{received / 1024d / 1024:0.0} MiB";
                        var eta = total is > 0 && rate > 0
                            ? TimeSpan.FromSeconds(Math.Max(0, (total.Value - received) / rate)).ToString(@"hh\:mm\:ss")
                            : null;
                        progress?.Report(new JobProgress(ProgressKind.Download, component, percent,
                            $"{rate / 1024d / 1024:0.0} MiB/s", eta, size));
                        lastPercent = percent;
                        nextReport = received + 8L * 1024 * 1024;
                    }
                }
                await output.FlushAsync(ct);
            }
            if (received < 1) throw new InvalidDataException("Unduhan file TTS kosong: " + Path.GetFileName(destination));
            File.Move(temporary, destination, true);
        }
        finally { TryDelete(temporary); }
    }

    private static bool IsValidAudio(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 1024; }
        catch (IOException) { return false; }
    }

    private static void EnsureGeneratedAudio(string path)
    {
        if (!IsValidAudio(path)) throw new InvalidOperationException("File audio TTS kosong atau gagal dibuat.");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Clipper98", "1.0"));
        return client;
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
}
