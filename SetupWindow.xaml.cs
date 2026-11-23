using System.Windows;

namespace Clipper98;

public partial class SetupWindow : Window
{
    public SetupWindow(string report)
    {
        InitializeComponent();
        ChecksBox.Text = report;
        RefreshStorageSummary();
    }

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
}
