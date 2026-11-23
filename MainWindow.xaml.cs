using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfMessageBox = System.Windows.MessageBox;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace Clipper98;

public partial class MainWindow : Window
{
    private static readonly HttpClient PreviewHttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly ClipPipeline _pipeline = new();
    private CancellationTokenSource? _cts;
    private AppSettings _settings = SettingsStore.Load();
    private ScanResult? _scan;
    private ClipperJobManifest? _manifest;
    private string? _manifestPath;
    private readonly List<ClipCandidate> _clips = [];
    private CancellationTokenSource? _thumbnailCts;
    private bool _windowReady;
    private bool _updatingClipControls;

    public MainWindow()
    {
        InitializeComponent();
        FontBox.ItemsSource = SubtitleFonts.LoadOptions();
        FontBox.SelectedItem = FontBox.Items.OfType<SubtitleFontOption>()
            .FirstOrDefault(option => option.FamilyName.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase))
            ?? FontBox.Items.OfType<SubtitleFontOption>().FirstOrDefault();
        SetCombo(FontStyleBox, "bold");
        OutputBox.Text = _settings.OutputFolder;
        SetCombo(SubtitleSourceBox, _settings.SubtitleSource);
        LoadSavedSubtitlePresets();
        FramePathText.Text = string.IsNullOrWhiteSpace(_settings.FramePngPath) ? "Belum ada frame" : _settings.FramePngPath;
        Loaded += (_, _) =>
        {
            _windowReady = true;
            LoadFramePreview();
            UpdatePreview();
        };
        WriteLog("Clipper 98 siap.");
    }

    private void CopyPromptButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (!IsYouTubeUrl(url))
        {
            SetStatus("STATUS: tempel URL YouTube terlebih dahulu.", true);
            return;
        }

        var requested = int.TryParse(CandidateCountBox.Text, out var count) ? Math.Clamp(count, 1, 20) : 5;
        CandidateCountBox.Text = requested.ToString();
        var prompt = $"@clipper {url} Buatkan Klip Viral sebanyak {requested} klip.";
        System.Windows.Clipboard.SetText(prompt);
        SetStatus("STATUS: prompt tersalin. Tempel di Gemini CLI dengan skill @clipper aktif.");
        WriteLog($"Prompt @clipper disalin untuk {requested} klip.");
    }

    private void ImportScriptButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "Pilih script kandidat dari Gemini CLI",
            Filter = "Python Clipper job (*.py)|*.py|Semua file (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ToggleBusy(true);
            ClipList.ItemsSource = null;
            _clips.Clear();
            _scan = null;
            _manifest = null;
            _manifestPath = null;
            var loaded = ClipperJobScript.Load(dialog.FileName);
            _manifest = loaded.Manifest;
            _manifestPath = dialog.FileName;
            _clips.AddRange(loaded.Clips);
            UrlBox.Text = loaded.Manifest.Source.Url;
            CandidateCountBox.Text = loaded.Manifest.RequestedCandidateCount.ToString();
            _scan = new ScanResult
            {
                Title = string.IsNullOrWhiteSpace(loaded.Manifest.Source.Title) ? "Video YouTube" : loaded.Manifest.Source.Title,
                Duration = loaded.Manifest.Source.DurationSeconds ?? 0,
                TranscriptSource = "Gemini CLI"
            };
            ClipList.ItemsSource = _clips;
            if (_clips.Count > 0) ClipList.SelectedIndex = 0;
            WorkProgress.IsIndeterminate = false;
            WorkProgress.Value = 100;
            SetStatus($"STATUS: {_clips.Count} kandidat dimuat dari Gemini CLI.");
            ClipDetail.Text = $"File: {Path.GetFileName(dialog.FileName)}\nJudul: {_scan.Title}\nTimestamp diedit di sini akan disimpan kembali ke script.";
            WriteLog($"Manifest Gemini dimuat: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            SetStatus("STATUS: file Gemini tidak bisa dimuat.", true);
            WriteLog("ERROR: " + ex.Message);
            WpfMessageBox.Show(ex.Message, "Import script Gemini", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private void ClipSelection_Click(object sender, RoutedEventArgs e) => SaveManifestEdits();

    private async void RenderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scan is null || !_clips.Any(c => c.IsSelected))
        {
            SetStatus("STATUS: centang setidaknya satu kandidat sebelum membuat klip.", true);
            return;
        }

        using var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            ToggleBusy(true);
            SaveManifestEdits();
            var scan = _scan;
            var selected = _clips.Where(c => c.IsSelected).ToList();
            var url = UrlBox.Text.Trim();
            var language = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "id";
            var subtitleSource = SelectedText(SubtitleSourceBox, "auto");
            var framePath = _settings.FramePngPath;
            if (!string.IsNullOrWhiteSpace(framePath) && !File.Exists(framePath))
                throw new FileNotFoundException("Frame PNG yang dipilih sudah tidak ditemukan. Pilih ulang frame atau hapus dari panel.", framePath);
            var outputFolder = OutputBox.Text;
            WorkProgress.IsIndeterminate = false; WorkProgress.Value = 0;
            var batchStarted = DateTime.Now;
            for (var i = 0; i < selected.Count; i++)
            {
                var index = i; // salinan lokal: callback progres berjalan async dan tidak boleh membaca variabel loop
                var clip = selected[index];
                SetStatus(index == 0
                    ? $"STATUS: klip 1/{selected.Count} — {clip.Name}; estimasi batch muncul setelah klip pertama."
                    : $"STATUS: klip {index + 1}/{selected.Count} — {clip.Name}; perkiraan sisa batch {TimeSpan.FromSeconds((DateTime.Now - batchStarted).TotalSeconds / index * (selected.Count - index)):mm\\:ss}.");
                WorkProgress.IsIndeterminate = true;
                var progress = new Progress<JobProgress>(p => OnRenderProgress(p, index, selected.Count));
                var file = await RenderViaCliAsync(url, scan, clip, ReadStyle(), outputFolder, language, subtitleSource, framePath, progress, cts.Token);
                WorkProgress.IsIndeterminate = false;
                WorkProgress.Value = (index + 1) * 100.0 / selected.Count;
                WriteLog("Selesai: " + file);
            }
            SetStatus($"STATUS: selesai. {selected.Count} klip berhasil dibuat.");
            WpfMessageBox.Show($"{selected.Count} klip selesai dibuat.\n\n{OutputBox.Text}", "Clipper 98", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            SetStatus("STATUS: render dibatalkan.", true);
            WriteLog("Render dibatalkan.");
        }
        catch (Exception ex)
        {
            SetStatus("STATUS: render gagal.", true);
            WriteLog("ERROR: " + ex.Message);
            WpfMessageBox.Show(ex.Message, "Clipper 98", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _cts = null;
            WorkProgress.IsIndeterminate = false;
            ToggleBusy(false);
        }
    }

    private void OnRenderProgress(JobProgress p, int index, int total)
    {
        if (p.Kind == ProgressKind.Download)
        {
            var parts = new List<string>();
            if (p.Percent is { } percent) parts.Add($"{percent:0}%");
            if (!string.IsNullOrWhiteSpace(p.Size)) parts.Add(p.Size);
            if (!string.IsNullOrWhiteSpace(p.Speed)) parts.Add(p.Speed);
            if (!string.IsNullOrWhiteSpace(p.Eta)) parts.Add("sisa " + p.Eta);

            if (p.Percent is { } value)
            {
                WorkProgress.IsIndeterminate = false;
                WorkProgress.Value = Math.Clamp((index + value / 100.0) / total * 100.0, 0, 100);
            }
            else
            {
                WorkProgress.IsIndeterminate = true; // persen tidak diketahui; jangan pura-pura presisi
            }
            SetStatus($"STATUS: unduh klip {index + 1}/{total} — {(parts.Count > 0 ? string.Join(" · ", parts) : "sedang berjalan")}");
            return;
        }

        WriteLog(p.Message);
        SetStatus($"STATUS: {p.Message} ({index + 1}/{total})");
        WorkProgress.IsIndeterminate = true;
    }

    private async Task<string> RenderViaCliAsync(
        string url, ScanResult scan, ClipCandidate clip, CaptionStyle style, string outputFolder,
        string language, string subtitleSource, string framePath, IProgress<JobProgress> progress, CancellationToken ct)
    {
        var engine = Path.Combine(AppContext.BaseDirectory, "Clipper98.exe");
        if (!File.Exists(engine))
            throw new InvalidOperationException("Clipper98.exe tidak ditemukan di folder aplikasi.");

        var jobRoot = Path.Combine(Path.GetTempPath(), "Clipper98", "cli-jobs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(jobRoot);
        using var activeLock = TempCacheService.AcquireActiveLock(jobRoot);
        var jobPath = Path.Combine(jobRoot, "render-job.json");
        var progressPath = Path.Combine(jobRoot, "progress.jsonl");
        var request = new RenderJobRequest
        {
            Url = url,
            Title = scan.Title,
            Clip = new ClipperJobClip
            {
                Id = clip.Name,
                StartSeconds = clip.Start,
                EndSeconds = clip.End,
                Selected = true,
                Hook = clip.Hook,
                Reason = clip.Reason,
                Caption = clip.Caption,
                Hashtags = clip.Hashtags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            },
            CaptionStyle = style,
            OutputFolder = outputFolder,
            Language = language,
            SubtitleSource = subtitleSource,
            FramePngPath = framePath,
            PreserveFullFrame = clip.PreserveFullFrame,
            ProgressFile = progressPath
        };
        var lineIndex = 0;
        string? outputPath = null;
        string? errorMessage = null;
        try
        {
            await File.WriteAllTextAsync(jobPath, System.Text.Json.JsonSerializer.Serialize(request), ct);
            var processTask = ProcessRunner.RunAsync(engine, ["--render-cli", jobPath], jobRoot, cancellationToken: ct);
            while (!processTask.IsCompleted)
            {
                ReadProgressLines();
                await Task.Delay(250, ct);
            }
            var result = await processTask;
            ReadProgressLines();
            if (result.ExitCode != 0)
                throw new InvalidOperationException(errorMessage ?? result.Tail() ?? "Proses render gagal.");
            if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
                throw new InvalidOperationException("Render selesai tetapi file output tidak ditemukan.");
            return outputPath;
        }
        finally
        {
            try { if (Directory.Exists(jobRoot)) Directory.Delete(jobRoot, true); } catch { }
        }

        void ReadProgressLines()
        {
            string[] lines;
            try { lines = File.Exists(progressPath) ? File.ReadAllLines(progressPath) : []; }
            catch (IOException) { return; }
            while (lineIndex < lines.Length)
            {
                var line = lines[lineIndex++];
                try
                {
                    var update = System.Text.Json.JsonSerializer.Deserialize<JobProgress>(
                        line, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (update is null) continue;
                    if (update.Kind == ProgressKind.Stage && update.Message.StartsWith("OUTPUT:", StringComparison.Ordinal))
                    {
                        outputPath = update.Message[7..];
                        continue;
                    }
                    if (update.Kind == ProgressKind.Stage && update.Message.StartsWith("ERROR:", StringComparison.Ordinal))
                    {
                        errorMessage = update.Message[6..].Trim();
                        continue;
                    }
                    progress.Report(update);
                }
                catch (System.Text.Json.JsonException) { }
            }
        }
    }
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is null) return;
        SetStatus("STATUS: membatalkan...");
        _cts.Cancel();
    }

    private void ClipList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClipList.SelectedItem is not ClipCandidate clip)
        {
            RenderButton.IsEnabled = false;
            SmartCropBox.IsEnabled = false;
            UpdatePreview();
            return;
        }
        ClipDetail.Text = $"{clip.TimeRange}\n{clip.Hook}\n{clip.Reason}" + (string.IsNullOrWhiteSpace(clip.Caption) ? "" : $"\nCaption: {clip.Caption}\n{clip.Hashtags}") + "\nKlip memakai timestamp ini persis.";
        StartTimeBox.Text = TimeSpan.FromSeconds(clip.Start).ToString(@"hh\:mm\:ss");
        EndTimeBox.Text = TimeSpan.FromSeconds(clip.End).ToString(@"hh\:mm\:ss");
        RenderButton.IsEnabled = _scan is not null;
        _updatingClipControls = true;
        SmartCropBox.IsEnabled = true;
        SmartCropBox.IsChecked = !clip.PreserveFullFrame;
        _updatingClipControls = false;
        UpdatePreview();
    }

    private void SmartCrop_Changed(object sender, RoutedEventArgs e)
    {
        if (!_windowReady || _updatingClipControls || ClipList.SelectedItem is not ClipCandidate clip) return;
        clip.PreserveFullFrame = SmartCropBox.IsChecked != true;
        WriteLog(clip.PreserveFullFrame
            ? $"{clip.Name}: mempertahankan seluruh frame; objek/slide tetap terlihat."
            : $"{clip.Name}: smart crop mengikuti wajah utama.");
        UpdatePreview();
    }

    private void AddTimestampButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scan is null) { SetStatus("STATUS: scan video terlebih dahulu.", true); return; }
        if (_clips.Count >= 20) { SetStatus("STATUS: batas maksimal 20 kandidat sudah tercapai.", true); return; }
        var start = 0d;
        foreach (var existing in _clips.OrderBy(clip => clip.Start))
            if (start < existing.End && start + 30 > existing.Start) start = existing.End;
        var remaining = _scan.Duration > 0 ? _scan.Duration - start : 30;
        var duration = Math.Min(30, remaining);
        if (duration <= 0)
        { SetStatus("STATUS: tidak ada rentang kosong untuk timestamp tambahan.", true); return; }
        var index = _clips.Count + 1;
        var clip = new ClipCandidate { Name = $"custom_{index:00}", Start = start, End = start + duration, Hook = "Timestamp custom", Reason = "Rentang waktu ditentukan manual." };
        _clips.Add(clip); ClipList.Items.Refresh(); ClipList.SelectedItem = clip;
        SaveManifestEdits();
    }

    private void UpdateTimestampButton_Click(object sender, RoutedEventArgs e)
    {
        if (ClipList.SelectedItem is not ClipCandidate clip || _scan is null) { SetStatus("STATUS: pilih kandidat untuk mengatur timestamp.", true); return; }
        if (!TimeSpan.TryParse(StartTimeBox.Text, out var start) || !TimeSpan.TryParse(EndTimeBox.Text, out var end) || start.TotalSeconds < 0 || end <= start || (_scan.Duration > 0 && end.TotalSeconds > _scan.Duration))
        { SetStatus("STATUS: pastikan timestamp HH:MM:SS valid, mulai sebelum selesai, dan berada di dalam video.", true); return; }
        if (_clips.Any(other => !ReferenceEquals(other, clip) && start.TotalSeconds < other.End && other.Start < end.TotalSeconds))
        { SetStatus("STATUS: timestamp ini bertumpang tindih dengan kandidat lain.", true); return; }
        clip.Start = start.TotalSeconds; clip.End = end.TotalSeconds;
        ClipList.Items.Refresh();
        SaveManifestEdits();
        SetStatus("STATUS: timestamp diperbarui.");
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Pilih folder hasil klip",
            SelectedPath = Directory.Exists(OutputBox.Text) ? OutputBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        OutputBox.Text = dialog.SelectedPath;
        _settings.OutputFolder = dialog.SelectedPath;
        SettingsStore.Save(_settings);
        SetStatus("STATUS: folder output disimpan.");
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(OutputBox.Text);
        Process.Start(new ProcessStartInfo { FileName = OutputBox.Text, UseShellExecute = true });
    }

    private void SetupButton_Click(object sender, RoutedEventArgs e)
    {
        var models = Path.Combine(AppContext.BaseDirectory, "tools", "models");
        var checks = new[]
        {
            ("yt-dlp.exe", ProcessRunner.FindTool("yt-dlp") is not null),
            ("Node.js untuk YouTube", ProcessRunner.FindTool("node") is not null || ProcessRunner.FindCommand("node") is not null),
            ("ffmpeg.exe", ProcessRunner.FindTool("ffmpeg") is not null),
            ("whisper-cli.exe", ProcessRunner.FindTool("whisper-cli") is not null),
            ("Whisper Large-v3-Turbo", File.Exists(Path.Combine(models, "ggml-large-v3-turbo.bin"))),
            ("Model face detection", File.Exists(Path.Combine(models, "haarcascade_frontalface_default.xml"))),
            ("OpenCV native runtime", File.Exists(Path.Combine(AppContext.BaseDirectory, "OpenCvSharpExtern.dll")) || File.Exists(Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "OpenCvSharpExtern.dll"))),
            ("Frame PNG", string.IsNullOrWhiteSpace(_settings.FramePngPath) || File.Exists(_settings.FramePngPath))
        };
        var report = string.Join(Environment.NewLine, checks.Select(item => $"{item.Item1}: {(item.Item2 ? "OK" : "belum ada")}"));
        var downloadable = new HashSet<string>(StringComparer.Ordinal)
        {
            "yt-dlp.exe", "Node.js untuk YouTube", "ffmpeg.exe", "whisper-cli.exe", "Whisper Large-v3-Turbo"
        };
        var missing = checks.Where(item => !item.Item2 && downloadable.Contains(item.Item1)).Select(item => item.Item1).ToList();
        new SetupWindow(report, missing) { Owner = this }.ShowDialog();
    }
    private async void UrlBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UrlBox.Text) || !IsYouTubeUrl(UrlBox.Text.Trim()))
        {
            _thumbnailCts?.Cancel();
            PreviewBackdrop.Source = null;
            PreviewFullFrame.Source = null;
            PreviewBackdrop.Effect = null;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            UpdatePreview();
            return;
        }
        SetStatus("STATUS: URL siap. Salin prompt untuk Gemini atau impor manifest .py.");
        var id = YouTubeVideoId(UrlBox.Text.Trim());
        if (id.Length > 0) await LoadYouTubeThumbnailAsync(id);
    }

    private async Task LoadYouTubeThumbnailAsync(string videoId)
    {
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        var cts = new CancellationTokenSource();
        _thumbnailCts = cts;
        try
        {
            await Task.Delay(350, cts.Token);
            var bytes = await PreviewHttpClient.GetByteArrayAsync($"https://i.ytimg.com/vi/{Uri.EscapeDataString(videoId)}/hqdefault.jpg", cts.Token);
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            if (cts.IsCancellationRequested) return;
            PreviewBackdrop.Source = bitmap;
            PreviewFullFrame.Source = bitmap;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            UpdatePreview();
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (ReferenceEquals(_thumbnailCts, cts))
                PreviewPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void SubtitleSourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_windowReady) return;
        _settings.SubtitleSource = SelectedText(SubtitleSourceBox, "auto");
        SettingsStore.Save(_settings);
        WriteLog(_settings.SubtitleSource == "whisper"
            ? "Transkrip disetel ke Whisper (transkrip ulang lokal)."
            : "Transkrip disetel ke subtitle YouTube otomatis, dengan Whisper sebagai cadangan.");
    }

    private void FrameUploadButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "Pilih frame PNG transparan 1080×1920",
            Filter = "PNG transparan (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ValidateFramePng(dialog.FileName);
            _settings.FramePngPath = dialog.FileName;
            SettingsStore.Save(_settings);
            FramePathText.Text = dialog.FileName;
            LoadFramePreview();
            WriteLog("Frame PNG diterapkan ke preview dan render: " + dialog.FileName);
        }
        catch (Exception ex)
        {
            SetStatus("STATUS: frame PNG tidak valid.", true);
            WpfMessageBox.Show(ex.Message, "Frame PNG", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportFontButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "Import font untuk subtitle",
            Filter = "Font TrueType/OpenType (*.ttf;*.otf)|*.ttf;*.otf",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var importedPath = SubtitleFonts.Import(dialog.FileName);
            var options = SubtitleFonts.LoadOptions();
            FontBox.ItemsSource = options;
            var importedOption = options.FirstOrDefault(option =>
                string.Equals(option.FilePath, importedPath, StringComparison.OrdinalIgnoreCase));
            FontBox.SelectedItem = importedOption;
            if (importedOption is not null)
                SetCombo(FontStyleBox, FontStyleTag(importedOption.IsBoldFace, importedOption.IsItalicFace));
            UpdatePreview();
            WriteLog($"Font diimpor: {importedOption?.FamilyName ?? Path.GetFileName(importedPath)} — {Path.GetFileName(importedPath)}");
        }
        catch (Exception ex)
        {
            SetStatus("STATUS: file font tidak bisa diimpor.", true);
            WpfMessageBox.Show(this, ex.Message, "Import font", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FontBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_windowReady || FontBox.SelectedItem is not SubtitleFontOption option) return;
        if (option.IsImported)
            SetCombo(FontStyleBox, FontStyleTag(option.IsBoldFace, option.IsItalicFace));
        UpdatePreview();
    }

    private void FrameRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.FramePngPath = string.Empty;
        SettingsStore.Save(_settings);
        PreviewFrame.Source = null;
        FramePathText.Text = "Belum ada frame";
        WriteLog("Frame PNG dilepas dari preview dan render.");
    }

    private void LoadFramePreview()
    {
        if (string.IsNullOrWhiteSpace(_settings.FramePngPath) || !File.Exists(_settings.FramePngPath))
        {
            PreviewFrame.Source = null;
            return;
        }
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(_settings.FramePngPath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        PreviewFrame.Source = bitmap;
    }

    private static void ValidateFramePng(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new InvalidDataException("PNG tidak memiliki frame gambar yang bisa dibaca.");
        var frame = decoder.Frames[0];
        if (frame.PixelWidth != 1080 || frame.PixelHeight != 1920)
            throw new InvalidDataException($"Resolusi frame harus 1080×1920. File ini {frame.PixelWidth}×{frame.PixelHeight}.");
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] < 255) return;
        throw new InvalidDataException("PNG frame harus punya area transparan agar video tetap terlihat.");
    }

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (PresetBox.SelectedItem is ComboBoxItem selected
            && selected.Tag?.ToString() is { } tag
            && tag.StartsWith("saved:", StringComparison.Ordinal))
        {
            var name = tag[6..];
            var saved = _settings.SubtitlePresets.FirstOrDefault(preset =>
                preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (saved is not null) ApplyCaptionStyle(saved.Style);
        }
        else switch (PresetBox.SelectedIndex)
        {
            case 1:
                SetFontFamily("Segoe UI"); SetCombo(FontStyleBox, "bold"); SetCombo(TextColorBox, "White"); FontSizeSlider.Value = 80; OutlineSlider.Value = 3; HighlightBox.IsChecked = false; break;
            case 2:
                SetFontFamily("Segoe UI"); SetCombo(FontStyleBox, "bold"); SetCombo(TextColorBox, "White"); FontSizeSlider.Value = 120; OutlineSlider.Value = 6; HighlightBox.IsChecked = true; SetCombo(HighlightColorBox, "Yellow"); break;
            default:
                SetFontFamily("Segoe UI"); SetCombo(FontStyleBox, "bold"); SetCombo(TextColorBox, "White"); FontSizeSlider.Value = 100; OutlineSlider.Value = 5; HighlightBox.IsChecked = true; SetCombo(HighlightColorBox, "Yellow"); break;
        }
        UpdatePresetDeleteButton();
        UpdatePreview();
    }

    private void LoadSavedSubtitlePresets()
    {
        _settings.SubtitlePresets ??= [];
        foreach (var preset in _settings.SubtitlePresets
                     .Where(preset => !string.IsNullOrWhiteSpace(preset.Name))
                     .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase))
        {
            PresetBox.Items.Add(new ComboBoxItem { Content = preset.Name, Tag = "saved:" + preset.Name });
        }
    }

    private void SavePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var name = PresetNameBox.Text.Trim();
        if (name.Length is < 1 or > 32)
        {
            SetStatus("STATUS: nama preset harus berisi 1–32 karakter.", true);
            return;
        }

        var existing = _settings.SubtitlePresets.FirstOrDefault(preset =>
            preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null
            && WpfMessageBox.Show($"Timpa preset ‘{existing.Name}’ dengan style saat ini?", "Simpan preset",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var saved = new SubtitleStylePreset { Name = name, Style = CopyStyle(ReadStyle()) };
        if (existing is null) _settings.SubtitlePresets.Add(saved);
        else
        {
            existing.Name = name;
            existing.Style = saved.Style;
        }
        SettingsStore.Save(_settings);
        var item = PresetBox.Items.OfType<ComboBoxItem>().FirstOrDefault(option =>
            option.Tag?.ToString()?.Equals("saved:" + name, StringComparison.OrdinalIgnoreCase) == true);
        if (item is null)
        {
            item = new ComboBoxItem { Content = name, Tag = "saved:" + name };
            PresetBox.Items.Add(item);
        }
        PresetBox.SelectedItem = item;
        PresetNameBox.Clear();
        UpdatePresetDeleteButton();
        WriteLog($"Preset subtitle ‘{name}’ disimpan.");
        SetStatus($"STATUS: preset ‘{name}’ disimpan.");
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is not ComboBoxItem item
            || item.Tag?.ToString() is not { } tag
            || !tag.StartsWith("saved:", StringComparison.Ordinal)) return;
        var name = tag[6..];
        _settings.SubtitlePresets.RemoveAll(preset => preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        SettingsStore.Save(_settings);
        PresetBox.Items.Remove(item);
        PresetBox.SelectedIndex = 0;
        UpdatePresetDeleteButton();
        WriteLog($"Preset subtitle ‘{name}’ dihapus.");
    }

    private void UpdatePresetDeleteButton()
    {
        DeletePresetButton.IsEnabled = PresetBox.SelectedItem is ComboBoxItem item
            && item.Tag?.ToString()?.StartsWith("saved:", StringComparison.Ordinal) == true;
    }

    private void ApplyCaptionStyle(CaptionStyle style)
    {
        var font = FontBox.Items.OfType<SubtitleFontOption>().FirstOrDefault(option =>
            !string.IsNullOrWhiteSpace(style.FontFilePath)
            && string.Equals(option.FilePath, style.FontFilePath, StringComparison.OrdinalIgnoreCase));
        if (font is not null) FontBox.SelectedItem = font;
        else SetFontFamily(style.FontName);
        SetCombo(FontStyleBox, FontStyleTag(style.FontBold, style.FontItalic));
        FontSizeSlider.Value = Math.Clamp(style.FontSize, (int)FontSizeSlider.Minimum, (int)FontSizeSlider.Maximum);
        SetCombo(TextColorBox, style.TextColor);
        SetCombo(HighlightColorBox, style.HighlightColor);
        OutlineSlider.Value = Math.Clamp(style.Outline, (int)OutlineSlider.Minimum, (int)OutlineSlider.Maximum);
        LetterSpacingSlider.Value = Math.Clamp(style.LetterSpacing, LetterSpacingSlider.Minimum, LetterSpacingSlider.Maximum);
        LineSpacingSlider.Value = Math.Clamp(style.LineSpacing, (int)LineSpacingSlider.Minimum, (int)LineSpacingSlider.Maximum);
        HighlightBox.IsChecked = style.HighlightEnabled;
        SetCombo(MaxLinesBox, style.MaxLines.ToString());
        SetCombo(TextCaseBox, style.TextCase);
        PositionYSlider.Value = Math.Clamp(style.PositionY, (int)PositionYSlider.Minimum, (int)PositionYSlider.Maximum);
    }

    private static CaptionStyle CopyStyle(CaptionStyle style) => new()
    {
        FontName = style.FontName,
        FontFilePath = style.FontFilePath,
        FontBold = style.FontBold,
        FontItalic = style.FontItalic,
        FontSize = style.FontSize,
        LetterSpacing = style.LetterSpacing,
        LineSpacing = style.LineSpacing,
        TextColor = style.TextColor,
        HighlightColor = style.HighlightColor,
        Outline = style.Outline,
        HighlightEnabled = style.HighlightEnabled,
        PositionY = style.PositionY,
        MaxLines = style.MaxLines,
        TextCase = style.TextCase
    };

    private void StyleControl_Changed(object sender, RoutedEventArgs e) => UpdatePreview();
    private void StyleControl_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdatePreview();

    private CaptionStyle ReadStyle() => new()
    {
        FontName = (FontBox?.SelectedItem as SubtitleFontOption)?.FamilyName ?? "Segoe UI",
        FontFilePath = (FontBox?.SelectedItem as SubtitleFontOption)?.FilePath ?? string.Empty,
        FontBold = SelectedText(FontStyleBox, "bold") is "bold" or "bold-italic",
        FontItalic = SelectedText(FontStyleBox, "regular") is "italic" or "bold-italic",
        FontSize = (int)(FontSizeSlider?.Value ?? 100),
        LetterSpacing = LetterSpacingSlider?.Value ?? 0,
        LineSpacing = (int)(LineSpacingSlider?.Value ?? 10),
        TextColor = SelectedText(TextColorBox, "White"),
        HighlightColor = SelectedText(HighlightColorBox, "Yellow"),
        HighlightEnabled = HighlightBox?.IsChecked == true,
        Outline = (int)(OutlineSlider?.Value ?? 4),
        PositionY = (int)(PositionYSlider?.Value ?? 1420),
        MaxLines = int.TryParse(SelectedText(MaxLinesBox, "2"), out var lines) ? lines : 2,
        TextCase = SelectedText(TextCaseBox, "sentence")
    };

    private void UpdatePreview()
    {
        if (!_windowReady || FontSizeLabel is null || FontBox is null || TextColorBox is null || HighlightColorBox is null || HighlightBox is null) return;
        var style = ReadStyle();
        FontSizeLabel.Text = style.FontSize + " px";
        PositionYLabel.Text = $"{style.PositionY} / 1920 px";
        LetterSpacingLabel.Text = $"{style.LetterSpacing:0.#} px";
        LineSpacingLabel.Text = $"{style.LineSpacing} px";
        PreviewCaption.Update(style, SubtitleFonts.ResolvePreviewFamily(FontBox.SelectedItem as SubtitleFontOption));

        var preserveFullFrame = ClipList.SelectedItem is ClipCandidate clip && clip.PreserveFullFrame;
        PreviewBackdrop.Effect = preserveFullFrame ? new System.Windows.Media.Effects.BlurEffect { Radius = 18 } : null;
        PreviewFullFrame.Source = PreviewBackdrop.Source;
        PreviewFullFrame.Visibility = preserveFullFrame && PreviewBackdrop.Source is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

        Canvas.SetTop(PreviewCaption, 0);
        PreviewPlaceholder.Visibility = PreviewBackdrop.Source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetFontFamily(string familyName) =>
        FontBox.SelectedItem = FontBox.Items.OfType<SubtitleFontOption>()
            .FirstOrDefault(option => option.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase)
                                      && !option.IsImported)
            ?? FontBox.Items.OfType<SubtitleFontOption>()
                .FirstOrDefault(option => option.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase));

    private static string FontStyleTag(bool bold, bool italic) => (bold, italic) switch
    {
        (true, true) => "bold-italic",
        (true, false) => "bold",
        (false, true) => "italic",
        _ => "regular"
    };

    private static string SelectedText(System.Windows.Controls.ComboBox? box, string fallback)
    {
        if (box?.SelectedItem is not ComboBoxItem item) return fallback;
        return item.Tag?.ToString() ?? item.Content?.ToString() ?? fallback;
    }
    private static void SetCombo(System.Windows.Controls.ComboBox box, string value) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Tag?.ToString(), value, StringComparison.Ordinal)
            || string.Equals(item.Content?.ToString(), value, StringComparison.Ordinal));

    private static bool IsYouTubeUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.ToLowerInvariant() is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtu.be" or "www.youtube-nocookie.com";

    private static string YouTubeVideoId(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return string.Empty;
        var host = uri.Host.ToLowerInvariant();
        if (host == "youtu.be") return uri.AbsolutePath.Trim('/').Split('/').FirstOrDefault() ?? string.Empty;
        foreach (var item in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            if (pair.Length == 2 && pair[0] == "v") return Uri.UnescapeDataString(pair[1]);
        }
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0] is "shorts" or "live" or "embed") return segments[1];
        return segments.LastOrDefault() ?? string.Empty;
    }

    private void ToggleBusy(bool busy)
    {
        UrlBox.IsEnabled = !busy;
        ImportScriptButton.IsEnabled = !busy;
        CopyPromptButton.IsEnabled = !busy;
        SubtitleSourceBox.IsEnabled = !busy;
        LanguageBox.IsEnabled = !busy;
        FrameUploadButton.IsEnabled = !busy;
        FrameRemoveButton.IsEnabled = !busy;
        PresetBox.IsEnabled = !busy;
        PresetNameBox.IsEnabled = !busy;
        SavePresetButton.IsEnabled = !busy;
        UpdatePresetDeleteButton();
        FontBox.IsEnabled = !busy;
        FontStyleBox.IsEnabled = !busy;
        TextCaseBox.IsEnabled = !busy;
        ImportFontButton.IsEnabled = !busy;
        FontSizeSlider.IsEnabled = !busy;
        TextColorBox.IsEnabled = !busy;
        HighlightColorBox.IsEnabled = !busy;
        OutlineSlider.IsEnabled = !busy;
        LetterSpacingSlider.IsEnabled = !busy;
        LineSpacingSlider.IsEnabled = !busy;
        HighlightBox.IsEnabled = !busy;
        MaxLinesBox.IsEnabled = !busy;
        PositionYSlider.IsEnabled = !busy;
        SmartCropBox.IsEnabled = !busy && ClipList.SelectedItem is not null;
        RenderButton.IsEnabled = !busy && ClipList.SelectedItem is not null;
        CancelButton.IsEnabled = busy;
        SetupButton.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }

    private void SaveManifestEdits()
    {
        if (_manifest is null || string.IsNullOrWhiteSpace(_manifestPath)) return;
        try
        {
            ClipperJobScript.Save(_manifestPath, _manifest, _clips);
            WriteLog("Perubahan pilihan/timestamp disimpan ke script Gemini.");
        }
        catch (Exception ex)
        {
            SetStatus("STATUS: perubahan script gagal disimpan.", true);
            WriteLog("ERROR simpan manifest: " + ex.Message);
        }
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = error ? WpfBrushes.LightSalmon : WpfBrushes.White;
    }
    private void WriteLog(string message)
    {
        if (LogBox is null) return;
        var color = message.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
            || message.Contains("gagal", StringComparison.OrdinalIgnoreCase)
                ? System.Windows.Media.Color.FromRgb(255, 112, 112)
                : message.Contains("warning", StringComparison.OrdinalIgnoreCase)
                    ? System.Windows.Media.Color.FromRgb(255, 204, 102)
                    : message.Contains("Selesai", StringComparison.OrdinalIgnoreCase)
                        || message.Contains("berhasil", StringComparison.OrdinalIgnoreCase)
                            ? System.Windows.Media.Color.FromRgb(126, 220, 155)
                            : System.Windows.Media.Color.FromRgb(210, 222, 238);
        var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 16 };
        paragraph.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss}] ")
        {
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(126, 145, 166))
        });
        paragraph.Inlines.Add(new Run(message) { Foreground = new SolidColorBrush(color) });
        LogBox.Document.Blocks.Add(paragraph);
        while (LogBox.Document.Blocks.Count > 3000)
            LogBox.Document.Blocks.Remove(LogBox.Document.Blocks.FirstBlock!);
        LogBox.ScrollToEnd();
    }
}
