using System.Diagnostics;
using System.Text;

namespace Clipper98;

public readonly record struct ProcessResult(int ExitCode, string Output, string Error)
{
    /// <summary>Baris terakhir yang berisi teks. Diutamakan dari stderr, tempat kebanyakan tool menulis error.</summary>
    public string LastLine => LastNonEmptyLine(Error) ?? LastNonEmptyLine(Output) ?? "Unknown error";

    /// <summary>Beberapa baris terakhir (default 6) digabung dengan " | ". Lebih informatif daripada satu baris untuk error FFmpeg.</summary>
    public string Tail(int lines = 6)
    {
        var source = string.IsNullOrWhiteSpace(Error) ? Output : Error;
        var tail = source.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .TakeLast(lines);
        return string.Join(" | ", tail);
    }

    private static string? LastNonEmptyLine(string text) =>
        text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .LastOrDefault(line => line.Length > 0);
}

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        Action<string>? onLine = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? AppContext.BaseDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        // ArgumentList mengurus quoting Windows sendiri, jadi tidak ada lagi Q() manual.
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info };
        process.Start();
        using var cancelRegistration = cancellationToken.Register(() => TryKill(process));

        var stdout = ReadLinesAsync(process.StandardOutput, onLine);
        var stderr = ReadLinesAsync(process.StandardError, onLine);
        await process.WaitForExitAsync();
        var output = await stdout;
        var error = await stderr;

        cancellationToken.ThrowIfCancellationRequested();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static async Task<string> ReadLinesAsync(StreamReader reader, Action<string>? onLine)
    {
        var text = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            text.AppendLine(line);
            onLine?.Invoke(line);
        }
        return text.ToString();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // proses sudah selesai atau tidak bisa dihentikan; abaikan
        }
    }

    public static string? FindTool(string name)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", name + ".exe"),
            Path.Combine(AppContext.BaseDirectory, name + ".exe"),
            Path.Combine(Environment.CurrentDirectory, "tools", name + ".exe"),
            Path.Combine(Environment.CurrentDirectory, name + ".exe")
        };
        var local = candidates.FirstOrDefault(File.Exists);
        if (local is not null) return local;

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pathCandidate = Path.Combine(folder, name + ".exe");
            if (File.Exists(pathCandidate)) return pathCandidate;
        }

        return null;
    }

    public static string? FindCommand(string name)
    {
        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidates = new List<string>();
        if (Path.HasExtension(name)) candidates.Add(name);
        else candidates.AddRange(extensions.Select(extension => name + extension));

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(folder, candidate);
                if (File.Exists(fullPath)) return fullPath;
            }
        return null;
    }

    public static string RequireTool(string name) =>
        FindTool(name) ?? throw new InvalidOperationException(
            $"File {name}.exe belum ditemukan. Letakkan di folder tools di samping Clipper98.exe, lalu buka ulang aplikasi.");
}
