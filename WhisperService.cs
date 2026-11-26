using System.Globalization;

namespace Clipper98;

/// <summary>Transkrip lokal dengan whisper.cpp (audio diekstrak lewat FFmpeg).</summary>
public sealed class WhisperService
{
    public async Task<List<SubtitleCue>> TranscribeAsync(
        string mediaFile, string language, string workDir, CancellationToken ct)
    {
        var ffmpeg = ProcessRunner.RequireTool("ffmpeg");
        var whisper = ProcessRunner.FindTool("whisper-cli")
            ?? throw new InvalidOperationException("whisper-cli.exe belum ditemukan di folder tools.");
        var modelsFolder = Path.Combine(AppContext.BaseDirectory, "tools", "models");
        const string modelFile = "ggml-large-v3-turbo.bin";
        var model = Path.Combine(modelsFolder, modelFile);
        if (!File.Exists(model))
            throw new InvalidOperationException($"Model Whisper belum ditemukan: tools\\models\\{modelFile}. Buka SETUP lalu unduh komponen yang kurang.");

        // Pastikan hasil unduhan punya jalur audio sebelum diekstrak.
        var probe = await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-i", mediaFile], workDir, null, ct);
        if (!(probe.Error + probe.Output).Contains("Audio:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Hasil unduhan tidak punya jalur audio, jadi tidak bisa ditranskrip. Info file: " + probe.Tail(10));

        var wav = Path.Combine(workDir, "clip-audio.wav");
        var extract = await ProcessRunner.RunAsync(
            ffmpeg, ["-y", "-i", mediaFile, "-vn", "-ac", "1", "-ar", "16000", wav], workDir, null, ct);
        if (extract.ExitCode != 0) throw new InvalidOperationException("Audio klip gagal diekstrak. " + extract.Tail());

        // "-l" selalu diberikan, termasuk "auto". Tanpa "-l", whisper.cpp memakai English sebagai default.
        var prefix = Path.Combine(workDir, "clip-transcript");
        var threadCount = Math.Max(1, Environment.ProcessorCount);
        var transcript = await ProcessRunner.RunAsync(
            whisper, ["-m", model, "-t", threadCount.ToString(CultureInfo.InvariantCulture), "-l", language,
                "-f", wav, "-osrt", "-ojf", "-of", prefix], workDir, null, ct);
        if (transcript.ExitCode != 0) throw new InvalidOperationException("Whisper gagal mentranskrip klip. " + transcript.Tail());

        var cues = SubtitleParser.ParseWordJson(prefix + ".json");
        if (cues.Count == 0) cues = SubtitleParser.ParseFile(prefix + ".srt");
        if (cues.Count == 0) throw new InvalidOperationException("Whisper selesai tetapi tidak menghasilkan teks.");
        return cues;
    }
}
