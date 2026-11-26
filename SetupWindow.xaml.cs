using System.Windows;

namespace Clipper98;

public partial class SetupWindow : Window
{
    private readonly IReadOnlyList<string> _missingComponents;
    private readonly AppSettings _settings;
    private readonly ComponentDownloadService _downloadService = new();
    private CancellationTokenSource? _downloadCancellation;
    private bool _themeReady;

    public SetupWindow(string report, IReadOnlyList<string> missingComponents, AppSettings settings)
    {
        InitializeComponent();
        _missingComponents = missingComponents;
        _settings = settings;
        NightModeCheckBox.IsChecked = settings.NightMode;
        _themeReady = true;
        ChecksBox.Text = report;
        DownloadMissingButton.Content = missingComponents.Count == 0
            ? "ALL COMPONENTS AVAILABLE"
            : $"DOWNLOAD MISSING COMPONENTS ({missingComponents.Count})";
        DownloadMissingButton.IsEnabled = missingComponents.Count > 0;
        DownloadStatusText.Text = missingComponents.Count == 0
            ? "All downloadable components are available."
            : "Missing components can be downloaded to the tools folder. The Whisper Large-v3-Turbo model is about 1.5 GiB.";
        RefreshStorageSummary();
    }

    private void NightMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_themeReady) return;
        _settings.NightMode = NightModeCheckBox.IsChecked == true;
        SettingsStore.Save(_settings);
        ThemeManager.Apply(_settings.NightMode);
    }

    private async void DownloadMissingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_missingComponents.Count == 0 || _downloadCancellation is not null) return;
        _downloadCancellation = new CancellationTokenSource();
        var cancellationToken = _downloadCancellation.Token;
        DownloadMissingButton.IsEnabled = false;
        ClearTempButton.IsEnabled = false;
        CloseButton.Visibility = Visibility.Collapsed;
        CancelDownloadButton.Visibility = Visibility.Visible;
        DownloadProgressBar.Visibility = Visibility.Visible;
        DownloadProgressBar.IsIndeterminate = true;
        DownloadStatusText.Text = "Preparing component download...";
        var progress = new Progress<ComponentDownloadProgress>(update =>
        {
            if (!IsLoaded || cancellationToken.IsCancellationRequested || _downloadCancellation?.Token != cancellationToken) return;
            DownloadStatusText.Text = $"Downloading {update.Component}: {update.Detail}";
            if (update.Percent is { } percent)
            {
                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressBar.Value = percent;
            }
        });

        try
        {
            await _downloadService.DownloadMissingAsync(_missingComponents, progress, cancellationToken);
            DownloadStatusText.Text = "Components downloaded. Everything is ready.";
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = 100;
        }
        catch (OperationCanceledException)
        {
            DownloadStatusText.Text = "Download canceled. Completed components remain available.";
        }
        catch (Exception ex)
        {
            DownloadStatusText.Text = "Download failed: " + ex.Message;
        }
        finally
        {
            if (_downloadCancellation is not null)
            {
                _downloadCancellation.Dispose();
                _downloadCancellation = null;
            }
            UpdateComponentCheckRows();
            CancelDownloadButton.Visibility = Visibility.Collapsed;
            var remaining = _missingComponents.Any(IsComponentMissing);
            DownloadMissingButton.Content = remaining ? "DOWNLOAD MISSING COMPONENTS AGAIN" : "ALL COMPONENTS AVAILABLE";
            DownloadMissingButton.IsEnabled = remaining;
            ClearTempButton.IsEnabled = TempCacheService.GetSummary().Bytes > 0;
            CloseButton.Visibility = Visibility.Visible;
        }
    }

    private void UpdateComponentCheckRows()
    {
        var statuses = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["yt-dlp.exe"] = ProcessRunner.FindTool("yt-dlp") is not null,
            ["Node.js for YouTube"] = ProcessRunner.FindTool("node") is not null || ProcessRunner.FindCommand("node") is not null,
            ["ffmpeg.exe"] = ProcessRunner.FindTool("ffmpeg") is not null,
            ["whisper-cli.exe"] = ProcessRunner.FindTool("whisper-cli") is not null,
            ["Whisper Large-v3-Turbo"] = File.Exists(Path.Combine(AppContext.BaseDirectory, "tools", "models", "ggml-large-v3-turbo.bin")),
            ["Face detection model"] = File.Exists(Path.Combine(AppContext.BaseDirectory, "tools", "models", "haarcascade_frontalface_default.xml"))
        };
        ChecksBox.Text = string.Join(Environment.NewLine, ChecksBox.Text.Split(Environment.NewLine)
            .Select(line =>
            {
                var separator = line.IndexOf(": ", StringComparison.Ordinal);
                if (separator < 0) return line;
                var name = line[..separator];
                return statuses.TryGetValue(name, out var exists)
                    ? $"{name}: {(exists ? "OK" : "Missing")}"
                    : line;
            }));
    }

    private bool IsComponentMissing(string component) => component switch
    {
        "yt-dlp.exe" => ProcessRunner.FindTool("yt-dlp") is null,
        "Node.js for YouTube" => ProcessRunner.FindTool("node") is null && ProcessRunner.FindCommand("node") is null,
        "ffmpeg.exe" => ProcessRunner.FindTool("ffmpeg") is null,
        "whisper-cli.exe" => ProcessRunner.FindTool("whisper-cli") is null,
        "Whisper Large-v3-Turbo" => !File.Exists(Path.Combine(AppContext.BaseDirectory, "tools", "models", "ggml-large-v3-turbo.bin")),
        "Face detection model" => !File.Exists(Path.Combine(AppContext.BaseDirectory, "tools", "models", "haarcascade_frontalface_default.xml")),
        _ => false
    };

    private void ClearTempButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = TempCacheService.Clear();
            CleanupStatusText.Text = result.RemovedBytes == 0
                ? "There are no temporary files to remove."
                : $"Removed {FormatBytes(result.RemovedBytes)} · {result.RemovedFiles} files · {result.RemovedFolders} folders.";
            if (result.SkippedFolders > 0)
                CleanupStatusText.Text += $" Skipped {result.SkippedFolders} active or protected folders.";
            RefreshStorageSummary();
        }
        catch (Exception ex)
        {
            CleanupStatusText.Text = "Cleanup failed: " + ex.Message;
        }
    }

    private void RefreshStorageSummary()
    {
        var summary = TempCacheService.GetSummary();
        StorageSummaryText.Text = $"{summary.FormattedSize} used · {summary.Files} files · {summary.Folders} folders";
        ClearTempButton.IsEnabled = summary.Bytes > 0;
    }

    private static string FormatBytes(long bytes) => bytes < 1024 * 1024
        ? $"{bytes / 1024.0:0.0} KB"
        : $"{bytes / 1024.0 / 1024.0:0.0} MB";

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void CancelDownloadButton_Click(object sender, RoutedEventArgs e) => _downloadCancellation?.Cancel();

    private void SetupWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e) => _downloadCancellation?.Cancel();
}
