using System.Text;
using System.Text.Json;
using System.Windows;

namespace Clipper98;

public partial class App : System.Windows.Application
{
    private static readonly object ProgressLock = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 2 && string.Equals(e.Args[0], "--render-cli", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await RunRenderJobAsync(e.Args[1]);
            Shutdown(exitCode);
            return;
        }

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static async Task<int> RunRenderJobAsync(string jobFile)
    {
        try
        {
            var json = await File.ReadAllTextAsync(jobFile);
            var request = JsonSerializer.Deserialize<RenderJobRequest>(json)
                ?? throw new InvalidOperationException("The render job is empty or invalid.");
            if (string.IsNullOrWhiteSpace(request.ProgressFile))
                throw new InvalidOperationException("The render job has no progress file.");

            Directory.CreateDirectory(Path.GetDirectoryName(request.ProgressFile)!);
            File.WriteAllText(request.ProgressFile, string.Empty, Encoding.UTF8);
            var progress = new FileProgress(request.ProgressFile);
            var output = await new ClipPipeline().RenderAsync(
                request.Url,
                new ScanResult { Title = request.Title },
                new ClipCandidate
                {
                    Name = request.Clip.Id,
                    Start = request.Clip.StartSeconds,
                    End = request.Clip.EndSeconds,
                    HeroFrameSeconds = request.HeroFrameSeconds ?? request.Clip.HeroFrameSeconds,
                    ThumbnailHook = request.ThumbnailHook ?? request.Clip.ThumbnailHook ?? request.Clip.Hook,
                    EnableOpeningMotion = request.EnableOpeningMotion,
                    Hook = request.Clip.Hook,
                    Reason = request.Clip.Reason,
                    Caption = request.Clip.Caption,
                    Hashtags = string.Join(" ", request.Clip.Hashtags),
                    PreserveFullFrame = request.PreserveFullFrame,
                    KeptSegments = request.Segments
                },
                request.CaptionStyle,
                request.OutputFolder,
                request.Language,
                request.SubtitleSource,
                request.FramePngPath,
                request.UseSubtitles,
                request.PreserveSourceAspectRatio,
                request.UseFourByFiveContentFrame,
                request.UseNarration,
                request.TtsOptions,
                progress,
                CancellationToken.None);
            progress.Report(JobProgress.Stage("OUTPUT:" + output));
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                var errorFile = "";
                if (File.Exists(jobFile))
                {
                    var request = JsonSerializer.Deserialize<RenderJobRequest>(await File.ReadAllTextAsync(jobFile));
                    errorFile = request?.ProgressFile ?? "";
                }
                if (!string.IsNullOrWhiteSpace(errorFile))
                    new FileProgress(errorFile).Report(JobProgress.Stage("ERROR: " + ex.Message));
            }
            catch { }
            return 1;
        }
    }

    private sealed class FileProgress(string path) : IProgress<JobProgress>
    {
        public void Report(JobProgress value)
        {
            var line = JsonSerializer.Serialize(value) + Environment.NewLine;
            lock (ProgressLock) File.AppendAllText(path, line, Encoding.UTF8);
        }
    }
}
