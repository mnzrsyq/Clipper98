using System.Windows;

namespace Clipper98;

public partial class SetupWindow : Window
{
    private readonly IReadOnlyList<string> _missingComponents;
    private readonly ComponentDownloadService _downloadService = new();
    private CancellationTokenSource? _downloadCancellation;

    public SetupWindow(string report, IReadOnlyList<string> missingComponents)
    {
        InitializeComponent();
        _missingComponents = missingComponents;
        ChecksBox.Text = report;
        DownloadMissingButton.Content = missingComponents.Count == 0
            ? "SEMUA KOMPONEN TERSEDIA"
            : $"UNDUH KOMPONEN KURANG ({missingComponents.Count})";
        DownloadMissingButton.IsEnabled = missingComponents.Count > 0;
        DownloadStatusText.Text = missingComponents.Count == 0
            ? "Semua komponen yang dapat diunduh sudah tersedia."
            : "Komponen yang belum ada bisa diunduh ke folder tools. Model Whisper Large-v3-Turbo berukuran sekitar 1,5 GiB.";
        RefreshStorageSummary();
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
        DownloadStatusText.Text = "Menyiapkan unduhan komponen...";
        var progress = new Progress<ComponentDownloadProgress>(update =>
        {
            if (!IsLoaded || cancellationToken.IsCancellationRequested || _downloadCancellation?.Token != cancellationToken) return;
            DownloadStatusText.Text = $"Mengunduh {update.Component}: {update.Detail}";
            if (update.Percent is { } percent)
            {
                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressBar.Value = percent;
            }
        });

        try
        {
            await _downloadService.DownloadMissingAsync(_missingComponents, progress, cancellationToken);
            DownloadStatusText.Text = "Komponen berhasil diunduh. Semua siap digunakan.";
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = 100;
        }
        catch (OperationCanceledException)
        {
            DownloadStatusText.Text = "Unduhan dibatalkan. Komponen yang sudah selesai tetap tersimpan.";
        }
        catch (Exception ex)
        {
            DownloadStatusText.Text = "Unduhan gagal: " + ex.Message;
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
            DownloadMissingButton.Content = remaining ? "UNDUH LAGI KOMPONEN YANG KURANG" : "SEMUA KOMPONEN TERSEDIA";
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
            ["Node.js untuk YouTube"] = ProcessRunner.FindTool("node") is not null || ProcessRunner.FindCommand("node") is not null,
            ["ffmpeg.exe"] = ProcessRunner.FindTool("ffmpeg") is not null,
            ["whisper-cli.exe"] = ProcessRunner.FindTool("whisper-cli") is not null,
            ["Whisper Large-v3-Turbo"] = File.Exists(Path.Combine(AppContext.BaseDirectory, "tools", "models", "ggml-large-v3-turbo.bin"))
        };
        ChecksBox.Text = string.Join(Environment.NewLine, ChecksBox.Text.Split(Environment.NewLine)
            .Select(line =>
            {
                var separator = line.IndexOf(": ", StringComparison.Ordinal);
                if (separator < 0) return line;
                var name = line[..separator];
                return statuses.TryGetValue(name, out var exists)
                    ? $"{name}: {(exists ? "OK" : "belum ada")}"
                    : line;
            }));
    }

    private bool IsComponentMissing(string component) => component switch
    {
        "yt-dlp.exe" => ProcessRunner.FindTool("yt-dlp") is null,
        "Node.js untuk YouTube" => ProcessRunner.FindTool("node") is null && ProcessRunner.FindCommand("node") is null,
        "ffmpeg.exe" => ProcessRunner.FindTool("ffmpeg") is null,
        "whisper-cli.exe" => ProcessRunner.FindTool("whisper-cli") is null,
        "Whisper Large-v3-Turbo" => !File.Exists(Path.Combine(AppContext.BaseDirectory, "tools", "models", "ggml-large-v3-turbo.bin")),
        _ => false
    };

    private void ClearTempButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = TempCacheService.Clear();
            CleanupStatusText.Text = result.RemovedBytes == 0
                ? "Tidak ada file temp yang bisa dihapus."
                : $"Dihapus {FormatBytes(result.RemovedBytes)} · {result.RemovedFiles} file · {result.RemovedFolders} folder.";
            if (result.SkippedFolders > 0)
                CleanupStatusText.Text += $" {result.SkippedFolders} folder aktif/terlindungi dilewati.";
            RefreshStorageSummary();
        }
        catch (Exception ex)
        {
            CleanupStatusText.Text = "Pembersihan gagal: " + ex.Message;
        }
    }

    private void RefreshStorageSummary()
    {
        var summary = TempCacheService.GetSummary();
        StorageSummaryText.Text = $"Terpakai {summary.FormattedSize} · {summary.Files} file · {summary.Folders} folder";
        ClearTempButton.IsEnabled = summary.Bytes > 0;
    }

    private static string FormatBytes(long bytes) => bytes < 1024 * 1024
        ? $"{bytes / 1024.0:0.0} KB"
        : $"{bytes / 1024.0 / 1024.0:0.0} MB";

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void CancelDownloadButton_Click(object sender, RoutedEventArgs e) => _downloadCancellation?.Cancel();

    private void SetupWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e) => _downloadCancellation?.Cancel();
}
