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
    private ClipCandidate? _metadataEditingClip;
    private CancellationTokenSource? _thumbnailCts;
    private bool _windowReady;
    private bool _updatingClipControls;
    private bool _settingManifestMode;

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
        SetCombo(TtsProviderBox, _settings.TtsProvider);
        if (TtsProviderBox.SelectedItem is null) SetCombo(TtsProviderBox, "fish");
        FishApiKeyBox.Password = _settings.FishApiKey;
        UpdateFishKeyVisibility();
        CondensedNarrationCheckBox.IsChecked = _settings.UseNarration;
        TtsRateSlider.Value = Math.Clamp(_settings.TtsRatePercent, -30, 30);
        TtsVolumeSlider.Value = Math.Clamp(_settings.TtsVolumePercent, 0, 150);
        OriginalVolumeSlider.Value = Math.Clamp(_settings.OriginalVolumePercent, 0, 150);
        TtsPitchSlider.Value = Math.Clamp(_settings.TtsPitchCents, -300, 300);
        UpdateTtsVoiceChoices(_settings.TtsVoice);
        LoadSavedSubtitlePresets();
        FramePathText.Text = string.IsNullOrWhiteSpace(_settings.FramePngPath) ? "Belum ada frame" : _settings.FramePngPath;
        Loaded += (_, _) =>
        {
            _windowReady = true;
            SaveTtsSettings();
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

        var prompt = BuildSkillPrompt(url);
        System.Windows.Clipboard.SetText(prompt);
        SetStatus(IsCondensedMode
            ? "STATUS: prompt Ringkas Video tersalin. Panggil /clipper-ringkas di Gemini CLI."
            : "STATUS: prompt klip pendek tersalin. Panggil /clipper di Gemini CLI.");
        WriteLog(IsCondensedMode ? "Prompt /clipper-ringkas disalin untuk meringkas video." : "Prompt /clipper disalin untuk membuat klip pendek.");
    }

    private string BuildSkillPrompt(string url)
    {
        if (IsCondensedMode)
        {
            var seconds = int.TryParse(SelectedText(TargetDurationBox, "0"), out var value) ? value : 0;
            var narration = CondensedNarrationCheckBox.IsChecked == true ? "on" : "off";
            return seconds == 0
                ? $"/clipper-ringkas {url} target_duration_seconds=0 narration={narration} Ringkas video ini menjadi satu video utuh lebih pendek dengan membuang bagian yang tidak perlu tanpa mempercepat. Jaga alur visual dan audio, buat narasi VO yang natural hanya jika diminta, dan selalu sertakan judul serta deskripsi YouTube."
                : $"/clipper-ringkas {url} target_duration_seconds={seconds} narration={narration} Ringkas video ini menjadi satu video utuh sekitar {seconds / 60} menit tanpa mempercepat. Jika target merusak alur, pertahankan konteks dan sarankan durasi lebih panjang. Buat narasi VO yang natural hanya jika diminta dan selalu sertakan judul serta deskripsi YouTube.";
        }

        var requested = int.TryParse(CandidateCountBox.Text, out var count) ? Math.Clamp(count, 1, 20) : 5;
        CandidateCountBox.Text = requested.ToString();
        return $"/clipper {url} Buatkan Klip Viral sebanyak {requested} klip. Untuk setiap kandidat, sertakan judul YouTube maksimal 100 karakter, deskripsi siap tempel, dan maksimal 3 hashtag relevan.";
    }

    private bool IsCondensedMode => SelectedText(ProcessingModeBox, "short_clips") == "condense";

    private void ProcessingModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShortClipOptionsPanel is null || CondensedOptionsPanel is null) return;
        if (!_settingManifestMode && _manifest is not null)
        {
            var loadedMode = _manifest.Mode.Equals("condense", StringComparison.OrdinalIgnoreCase) ? "condense" : "short_clips";
            if (!string.Equals(SelectedText(ProcessingModeBox, "short_clips"), loadedMode, StringComparison.Ordinal))
            {
                SaveManifestEdits();
                _manifest = null;
                _manifestPath = null;
                _clips.Clear();
                ClipList.ItemsSource = null;
                _scan = null;
                ClipDetail.Text = "Mode berubah. Salin prompt untuk mode ini, lalu impor manifest hasil Gemini.";
                RenderButton.IsEnabled = false;
            }
        }
        var condensed = IsCondensedMode;
        ShortClipOptionsPanel.Visibility = condensed ? Visibility.Collapsed : Visibility.Visible;
        CondensedOptionsPanel.Visibility = condensed ? Visibility.Visible : Visibility.Collapsed;
        TimestampEditPanel.Visibility = condensed ? Visibility.Collapsed : Visibility.Visible;
        ClipListGroupBox.Header = condensed ? "VIDEO RINGKAS" : "KANDIDAT KLIP";
        UpdateModeDependentControls();
        UpdatePreview();
    }

    private void CondensedSubtitle_Changed(object sender, RoutedEventArgs e)
    {
        if (LanguageBox is null) return;
        UpdateModeDependentControls();
        UpdatePreview();
    }

    private void CondensedAspect_Changed(object sender, SelectionChangedEventArgs e) => UpdateModeDependentControls();

    private void UpdateModeDependentControls()
    {
        if (SubtitleSettingsPanel is null) return;
        var condensed = IsCondensedMode;
        var useSubtitles = !condensed || CondensedSubtitleCheckBox.IsChecked == true;
        CondensedSubtitleCheckBox.Visibility = condensed ? Visibility.Visible : Visibility.Collapsed;
        CondensedSubtitleCheckBox.IsEnabled = _cts is null && condensed;
        SubtitleSettingsPanel.IsEnabled = useSubtitles;
        SubtitleSourceBox.IsEnabled = _cts is null && !condensed;
        LanguageBox.IsEnabled = _cts is null && useSubtitles;
        var portraitOutput = condensed && SelectedText(CondensedAspectBox, "source") == "portrait";
        FramingHeaderText.Text = condensed ? "Framing Video Ringkas" : "Framing Klip Terpilih";
        SmartCropBox.ToolTip = portraitOutput
            ? "Dicentang: area utama 4:5 mengikuti wajah. Tidak dicentang: area utama 4:5 tetap di tengah. Output tetap 9:16 dengan latar blur."
            : "Dicentang: crop 9:16 mengikuti wajah utama. Tidak dicentang: seluruh frame video terlihat dengan latar blur.";
        SmartCropHelpText.Text = portraitOutput
            ? "Isi video ditampilkan dalam crop 4:5 di tengah kanvas 9:16; ruang atas dan bawah memakai background blur. Jika wajah tidak terdeteksi, seluruh gambar dipasang di area 4:5."
            : "Jika Smart Crop aktif dan wajah tak terdeteksi selama 2 detik atau lebih, tampilan sementara beralih ke video utuh dengan latar blur.";
        SmartCropBox.IsEnabled = _cts is null && ClipList.SelectedItem is not null && (!condensed || portraitOutput);
        FrameUploadButton.IsEnabled = _cts is null && !condensed;
        FrameRemoveButton.IsEnabled = _cts is null && !condensed;
        CondensedNarrationCheckBox.IsEnabled = _cts is null && condensed;
        NarrationSettingsPanel.IsEnabled = _cts is null && condensed && CondensedNarrationCheckBox.IsChecked == true;
        NarrationSettingsExpander.IsEnabled = _cts is null && condensed && CondensedNarrationCheckBox.IsChecked == true;
    }

    private void NarrationEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (CondensedNarrationCheckBox is null) return;
        _settings.UseNarration = CondensedNarrationCheckBox.IsChecked == true;
        if (_windowReady) SettingsStore.Save(_settings);
        UpdateModeDependentControls();
    }

    private void TtsProvider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TtsVoiceBox is null) return;
        var previousVoice = _settings.TtsVoice;
        _settings.TtsProvider = SelectedText(TtsProviderBox, "fish");
        UpdateTtsVoiceChoices(previousVoice);
        UpdateFishKeyVisibility();
        if (_windowReady) SettingsStore.Save(_settings);
    }

    private void UpdateTtsVoiceChoices(string preferredVoice)
    {
        if (TtsVoiceBox is null) return;
        var provider = SelectedText(TtsProviderBox, _settings.TtsProvider);
        var voices = provider == "edge"
            ? new (string Label, string Id)[]
            {
                ("Gadis · Indonesia", "id-ID-GadisNeural"),
                ("Ardi · Indonesia", "id-ID-ArdiNeural")
            }
            : provider == "fish"
            ? new (string Label, string Id)[]
            {
                ("Budi · Indonesia", "budi")
            }
            : new (string Label, string Id)[] { ("Budi · Indonesia", "budi") };

        TtsVoiceBox.Items.Clear();
        foreach (var voice in voices)
            TtsVoiceBox.Items.Add(new ComboBoxItem { Content = voice.Label, Tag = voice.Id });
        SetCombo(TtsVoiceBox, preferredVoice);
        if (TtsVoiceBox.SelectedItem is null && TtsVoiceBox.Items.Count > 0)
            TtsVoiceBox.SelectedIndex = 0;
        _settings.TtsVoice = SelectedText(TtsVoiceBox, voices[0].Id);
    }

    private void TtsControl_Changed(object sender, SelectionChangedEventArgs e) => SaveTtsSettings();
    private void TtsControl_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => SaveTtsSettings();

    private void FishApiKey_Changed(object sender, RoutedEventArgs e)
    {
        if (!_windowReady || FishApiKeyBox is null) return;
        _settings.FishApiKey = FishApiKeyBox.Password.Trim();
        SettingsStore.Save(_settings);
    }

    private void UpdateFishKeyVisibility()
    {
        var isFish = SelectedText(TtsProviderBox, "fish") == "fish";
        FishApiKeyLabel.Visibility = isFish ? Visibility.Visible : Visibility.Collapsed;
        FishApiKeyBox.Visibility = isFish ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveTtsSettings()
    {
        if (!_windowReady || TtsProviderBox is null || TtsVoiceBox is null
            || TtsRateSlider is null || TtsVolumeSlider is null || OriginalVolumeSlider is null || TtsPitchSlider is null
            || TtsRateLabel is null || TtsVolumeLabel is null || OriginalVolumeLabel is null || TtsPitchLabel is null) return;
        _settings.TtsProvider = SelectedText(TtsProviderBox, "fish");
        _settings.TtsVoice = SelectedText(TtsVoiceBox, "budi");
        _settings.TtsRatePercent = (int)TtsRateSlider.Value;
        _settings.TtsVolumePercent = (int)TtsVolumeSlider.Value;
        _settings.OriginalVolumePercent = (int)OriginalVolumeSlider.Value;
        _settings.TtsPitchCents = (int)TtsPitchSlider.Value;
        TtsRateLabel.Text = _settings.TtsRatePercent == 0 ? "Normal" : $"{_settings.TtsRatePercent:+#;-#;0}%";
        TtsVolumeLabel.Text = $"{_settings.TtsVolumePercent}%";
        OriginalVolumeLabel.Text = $"{_settings.OriginalVolumePercent}%";
        TtsPitchLabel.Text = _settings.TtsPitchCents == 0
            ? "Normal"
            : $"{_settings.TtsPitchCents / 100.0:+0.##;-0.##} semiton";
        if (_windowReady) SettingsStore.Save(_settings);
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
            _settingManifestMode = true;
            _manifest = loaded.Manifest;
            _manifestPath = dialog.FileName;
            _clips.AddRange(loaded.Clips);
            ProcessingModeBox.SelectedItem = ProcessingModeBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), loaded.Manifest.Mode, StringComparison.OrdinalIgnoreCase));
            _settingManifestMode = false;
            if (loaded.Manifest.Mode.Equals("condense", StringComparison.OrdinalIgnoreCase))
            {
                TargetDurationBox.SelectedItem = TargetDurationBox.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => item.Tag?.ToString() == loaded.Manifest.TargetDurationSeconds.ToString())
                    ?? TargetDurationBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
            }
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
            SetStatus(loaded.Manifest.Mode.Equals("condense", StringComparison.OrdinalIgnoreCase)
                ? $"STATUS: video ringkas dimuat; {_clips[0].KeptSegments.Count} bagian akan disambung."
                : $"STATUS: {_clips.Count} kandidat dimuat dari Gemini CLI.");
            ClipDetail.Text = loaded.Manifest.Mode.Equals("condense", StringComparison.OrdinalIgnoreCase)
                ? BuildCondensedDetail(_clips[0])
                : $"File: {Path.GetFileName(dialog.FileName)}\nJudul: {_scan.Title}\nTimestamp diedit di sini akan disimpan kembali ke script.";
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
            ValidateCustomColors();
            CommitUploadMetadata();
            SaveManifestEdits();
            var scan = _scan;
            var selected = _clips.Where(c => c.IsSelected).ToList();
            var url = UrlBox.Text.Trim();
            var language = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "id";
            var isCondensed = _manifest?.Mode.Equals("condense", StringComparison.OrdinalIgnoreCase) == true;
            var useSubtitles = !isCondensed || CondensedSubtitleCheckBox.IsChecked == true;
            var useNarration = isCondensed && CondensedNarrationCheckBox.IsChecked == true;
            if (useNarration && selected.Any(clip => clip.KeptSegments.Count == 0
                    || clip.KeptSegments.Any(segment => string.IsNullOrWhiteSpace(segment.Narration))))
                throw new InvalidOperationException("Manifest belum memiliki narasi untuk semua potongan. Salin ulang Prompt Gemini dengan opsi narasi aktif, lalu impor manifest terbaru.");
            var subtitleSource = isCondensed ? "whisper" : SelectedText(SubtitleSourceBox, "auto");
            var preserveSourceAspect = isCondensed && SelectedText(CondensedAspectBox, "source") == "source";
            var useFourByFiveFrame = isCondensed && !preserveSourceAspect;
            var framePath = isCondensed ? string.Empty : _settings.FramePngPath;
            var ttsOptions = ReadTtsOptions();
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
                var file = await RenderViaCliAsync(url, scan, clip, ReadStyle(), outputFolder, language, subtitleSource,
                    framePath, useSubtitles, preserveSourceAspect, useFourByFiveFrame, useNarration, ttsOptions, progress, cts.Token);
                WorkProgress.IsIndeterminate = false;
                WorkProgress.Value = (index + 1) * 100.0 / selected.Count;
                WriteLog("Selesai: " + file);
            }
            SetStatus(isCondensed ? "STATUS: video ringkas selesai dibuat." : $"STATUS: selesai. {selected.Count} klip berhasil dibuat.");
            WpfMessageBox.Show(isCondensed
                ? $"Video ringkas selesai dibuat.\n\n{OutputBox.Text}"
                : $"{selected.Count} klip selesai dibuat.\n\n{OutputBox.Text}", "Clipper 98", MessageBoxButton.OK, MessageBoxImage.Information);
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
            var transfer = _manifest?.Mode.Equals("condense", StringComparison.OrdinalIgnoreCase) == true
                ? p.Message
                : $"unduh klip {index + 1}/{total}";
            SetStatus($"STATUS: {transfer} — {(parts.Count > 0 ? string.Join(" · ", parts) : "sedang berjalan")}");
            return;
        }

        WriteLog(p.Message);
        SetStatus($"STATUS: {p.Message} ({index + 1}/{total})");
        WorkProgress.IsIndeterminate = true;
    }

    private async Task<string> RenderViaCliAsync(
        string url, ScanResult scan, ClipCandidate clip, CaptionStyle style, string outputFolder,
        string language, string subtitleSource, string framePath, bool useSubtitles, bool preserveSourceAspect,
        bool useFourByFiveFrame, bool useNarration, TtsOptions ttsOptions,
        IProgress<JobProgress> progress, CancellationToken ct)
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
            Segments = clip.KeptSegments,
            UseSubtitles = useSubtitles,
            UseNarration = useNarration,
            TtsOptions = ttsOptions,
            PreserveSourceAspectRatio = preserveSourceAspect,
            UseFourByFiveContentFrame = useFourByFiveFrame,
            CaptionStyle = style,
            OutputFolder = outputFolder,
            Language = language,
            SubtitleSource = subtitleSource,
            FramePngPath = framePath,
            PreserveFullFrame = clip.PreserveFullFrame,
            ProgressFile = progressPath
        };
        long progressReadPosition = 0;
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
            try
            {
                if (!File.Exists(progressPath)) return;

                using var stream = new FileStream(
                    progressPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (progressReadPosition > stream.Length)
                    progressReadPosition = 0;

                stream.Position = progressReadPosition;
                var remaining = stream.Length - progressReadPosition;
                if (remaining <= 0) return;
                if (remaining > int.MaxValue)
                    throw new InvalidDataException("File progress terlalu besar.");

                var bytes = new byte[(int)remaining];
                var bytesRead = 0;
                while (bytesRead < bytes.Length)
                {
                    var count = stream.Read(bytes, bytesRead, bytes.Length - bytesRead);
                    if (count == 0) break;
                    bytesRead += count;
                }

                if (bytesRead == 0) return;
                var newlineIndex = Array.LastIndexOf(bytes, (byte)'\n', bytesRead - 1, bytesRead);
                if (newlineIndex < 0) return;

                var text = System.Text.Encoding.UTF8.GetString(bytes, 0, newlineIndex);
                if (progressReadPosition == 0)
                    text = text.TrimStart('\uFEFF');
                progressReadPosition += newlineIndex + 1;

                foreach (var rawLine in text.Split('\n'))
                {
                    var line = rawLine.TrimEnd('\r');
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
            catch (IOException) { return; }
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
        CommitUploadMetadata();
        if (ClipList.SelectedItem is not ClipCandidate clip)
        {
            SetUploadMetadataEditor(null);
            RenderButton.IsEnabled = false;
            TimestampEditPanel.Visibility = IsCondensedMode ? Visibility.Collapsed : Visibility.Visible;
            UpdateModeDependentControls();
            UpdatePreview();
            return;
        }
        SetUploadMetadataEditor(clip);
        ClipDetail.Text = clip.KeptSegments.Count > 0
            ? BuildCondensedDetail(clip)
            : $"{clip.TimeRange}\n{clip.Hook}\n{clip.Reason}" + (string.IsNullOrWhiteSpace(clip.Caption) ? "" : $"\nCaption: {clip.Caption}\n{clip.Hashtags}") + "\nKlip memakai timestamp ini persis.";
        StartTimeBox.Text = TimeSpan.FromSeconds(clip.Start).ToString(@"hh\:mm\:ss");
        EndTimeBox.Text = TimeSpan.FromSeconds(clip.End).ToString(@"hh\:mm\:ss");
        RenderButton.IsEnabled = _scan is not null;
        _updatingClipControls = true;
        SmartCropBox.IsChecked = !clip.PreserveFullFrame;
        TimestampEditPanel.Visibility = clip.KeptSegments.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        _updatingClipControls = false;
        UpdatePreview();
    }

    private string BuildCondensedDetail(ClipCandidate clip)
    {
        var keptSeconds = clip.KeptSegments.Sum(segment => segment.DurationSeconds);
        var target = _manifest?.TargetDurationSeconds > 0
            ? $"\nTarget: sekitar {FormatDuration(_manifest.TargetDurationSeconds)} (perkiraan; alur utuh didahulukan)"
            : string.Empty;
        var parts = clip.KeptSegments.Select((segment, index) =>
            $"{index + 1}. {segment.TimeRange} — {segment.Reason}");
        return $"{clip.KeptSegments.Count} bagian dipertahankan · hasil sekitar {FormatDuration(keptSeconds)}{target}\n\n{string.Join("\n", parts)}";
    }

    private static string FormatDuration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(seconds);
        return $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}";
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

    private void SetUploadMetadataEditor(ClipCandidate? clip)
    {
        _metadataEditingClip = clip;
        YouTubeTitleBox.Text = clip?.YouTubeTitle ?? string.Empty;
        YouTubeDescriptionBox.Text = clip?.YouTubeDescription ?? string.Empty;
        UpdateUploadMetadataCounters();
    }

    private void CommitUploadMetadata()
    {
        if (_metadataEditingClip is null) return;
        var title = YouTubeTitleBox.Text.Trim();
        var description = YouTubeDescriptionBox.Text.Trim();
        if (_metadataEditingClip.YouTubeTitle == title && _metadataEditingClip.YouTubeDescription == description) return;
        _metadataEditingClip.YouTubeTitle = title;
        _metadataEditingClip.YouTubeDescription = description;
        SaveManifestEdits();
    }

    private void UploadMetadata_TextChanged(object sender, TextChangedEventArgs e) => UpdateUploadMetadataCounters();

    private void UploadMetadata_LostFocus(object sender, RoutedEventArgs e) => CommitUploadMetadata();

    private void UpdateUploadMetadataCounters()
    {
        var titleLength = new System.Globalization.StringInfo(YouTubeTitleBox.Text).LengthInTextElements;
        var descriptionLength = new System.Globalization.StringInfo(YouTubeDescriptionBox.Text).LengthInTextElements;
        YouTubeTitleCountText.Text = $"{titleLength} / 100";
        YouTubeTitleCountText.Foreground = titleLength > 100 ? WpfBrushes.IndianRed : WpfBrushes.Gray;
        YouTubeDescriptionCountText.Text = $"{descriptionLength} / 5000";
        YouTubeDescriptionCountText.Foreground = descriptionLength > 5000 ? WpfBrushes.IndianRed : WpfBrushes.Gray;
        CopyYouTubeTitleButton.IsEnabled = ClipList.SelectedItem is not null && titleLength is > 0 and <= 100;
        CopyYouTubeDescriptionButton.IsEnabled = ClipList.SelectedItem is not null && descriptionLength is > 0 and <= 5000;
    }

    private void CopyYouTubeTitleButton_Click(object sender, RoutedEventArgs e)
    {
        CommitUploadMetadata();
        var title = YouTubeTitleBox.Text.Trim();
        var length = new System.Globalization.StringInfo(title).LengthInTextElements;
        if (length is 0 or > 100)
        {
            SetStatus("STATUS: judul harus berisi 1–100 karakter.", true);
            return;
        }
        System.Windows.Clipboard.SetText(title);
        SetStatus($"STATUS: judul ({length}/100 karakter) disalin.");
    }

    private void CopyYouTubeDescriptionButton_Click(object sender, RoutedEventArgs e)
    {
        CommitUploadMetadata();
        var description = YouTubeDescriptionBox.Text.Trim();
        var length = new System.Globalization.StringInfo(description).LengthInTextElements;
        if (length is 0 or > 5000)
        {
            SetStatus("STATUS: deskripsi harus berisi 1–5.000 karakter.", true);
            return;
        }
        System.Windows.Clipboard.SetText(description);
        SetStatus($"STATUS: deskripsi dan hashtag ({length}/5000 karakter) disalin.");
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
            "yt-dlp.exe", "Node.js untuk YouTube", "ffmpeg.exe", "whisper-cli.exe", "Whisper Large-v3-Turbo", "Model face detection"
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
        SetColorChoice(TextColorBox, TextColorHexBox, style.TextColor);
        SetColorChoice(HighlightColorBox, HighlightColorHexBox, style.HighlightColor);
        SetCombo(HighlightModeBox, style.HighlightMode);
        OutlineSlider.Value = Math.Clamp(style.Outline, (int)OutlineSlider.Minimum, (int)OutlineSlider.Maximum);
        LetterSpacingSlider.Value = Math.Clamp(style.LetterSpacing, LetterSpacingSlider.Minimum, LetterSpacingSlider.Maximum);
        LineSpacingSlider.Value = Math.Clamp(style.LineSpacing, (int)LineSpacingSlider.Minimum, (int)LineSpacingSlider.Maximum);
        HighlightBox.IsChecked = style.HighlightEnabled;
        SetCombo(MaxLinesBox, style.WordByWord ? "word" : style.MaxLines.ToString());
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
        HighlightMode = style.HighlightMode,
        Outline = style.Outline,
        HighlightEnabled = style.HighlightEnabled,
        WordByWord = style.WordByWord,
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
        TextColor = ReadColorChoice(TextColorBox, TextColorHexBox, "White"),
        HighlightColor = ReadColorChoice(HighlightColorBox, HighlightColorHexBox, "Yellow"),
        HighlightMode = SelectedText(HighlightModeBox, "color"),
        HighlightEnabled = HighlightBox?.IsChecked == true,
        Outline = (int)(OutlineSlider?.Value ?? 4),
        PositionY = (int)(PositionYSlider?.Value ?? 1420),
        MaxLines = int.TryParse(SelectedText(MaxLinesBox, "2"), out var lines) ? lines : 2,
        WordByWord = SelectedText(MaxLinesBox, "2") == "word",
        TextCase = SelectedText(TextCaseBox, "sentence")
    };

    private TtsOptions ReadTtsOptions() => new()
    {
        Provider = SelectedText(TtsProviderBox, "fish"),
        Voice = SelectedText(TtsVoiceBox, "budi"),
        RatePercent = Math.Clamp((int)TtsRateSlider.Value, -30, 30),
        VolumePercent = Math.Clamp((int)TtsVolumeSlider.Value, 0, 150),
        OriginalVolumePercent = Math.Clamp((int)OriginalVolumeSlider.Value, 0, 150),
        PitchCents = Math.Clamp((int)TtsPitchSlider.Value, -300, 300)
    };

    private void ColorChoice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TextColorHexBox is null || HighlightColorHexBox is null) return;
        TextColorHexBox.IsEnabled = SelectedText(TextColorBox, "White") == "custom";
        HighlightColorHexBox.IsEnabled = SelectedText(HighlightColorBox, "Yellow") == "custom";
        UpdatePreview();
    }

    private void ColorHex_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private static void SetColorChoice(System.Windows.Controls.ComboBox box, System.Windows.Controls.TextBox hexBox, string color)
    {
        var isNamed = box.Items.OfType<ComboBoxItem>().Any(item =>
            item.Tag?.ToString()?.Equals(color, StringComparison.OrdinalIgnoreCase) == true);
        if (isNamed)
        {
            SetCombo(box, color);
            hexBox.Text = ColorToHex(color);
        }
        else
        {
            SetCombo(box, "custom");
            hexBox.Text = color;
        }
    }

    private static string ReadColorChoice(System.Windows.Controls.ComboBox box, System.Windows.Controls.TextBox hexBox, string fallback)
    {
        if (SelectedText(box, fallback) != "custom") return SelectedText(box, fallback);
        var value = hexBox.Text.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^#[0-9A-Fa-f]{6}$")) return fallback;
        return value.ToUpperInvariant();
    }

    private void ValidateCustomColors()
    {
        if (SelectedText(TextColorBox, "White") == "custom"
            && !System.Text.RegularExpressions.Regex.IsMatch(TextColorHexBox.Text.Trim(), @"^#[0-9A-Fa-f]{6}$"))
            throw new InvalidOperationException("Warna teks kustom harus memakai format HEX #RRGGBB.");
        if (SelectedText(HighlightColorBox, "Yellow") == "custom"
            && !System.Text.RegularExpressions.Regex.IsMatch(HighlightColorHexBox.Text.Trim(), @"^#[0-9A-Fa-f]{6}$"))
            throw new InvalidOperationException("Warna highlight kustom harus memakai format HEX #RRGGBB.");
    }

    private static string ColorToHex(string value) => value switch
    {
        "Yellow" => "#FFD329",
        "Pink" => "#FF4FA3",
        "Red" => "#FF5A4B",
        _ => "#FFFFFF"
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
        PreviewCaption.Visibility = IsCondensedMode && CondensedSubtitleCheckBox.IsChecked != true
            ? Visibility.Collapsed
            : Visibility.Visible;

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
        ProcessingModeBox.IsEnabled = !busy;
        TargetDurationBox.IsEnabled = !busy;
        CondensedAspectBox.IsEnabled = !busy;
        CondensedSubtitleCheckBox.IsEnabled = !busy;
        TtsProviderBox.IsEnabled = !busy;
        TtsVoiceBox.IsEnabled = !busy;
        FishApiKeyBox.IsEnabled = !busy;
        TtsRateSlider.IsEnabled = !busy;
        TtsVolumeSlider.IsEnabled = !busy;
        OriginalVolumeSlider.IsEnabled = !busy;
        TtsPitchSlider.IsEnabled = !busy;
        ImportScriptButton.IsEnabled = !busy;
        CopyPromptButton.IsEnabled = !busy;
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
        YouTubeMetadataExpander.IsEnabled = !busy;
        RenderButton.IsEnabled = !busy && ClipList.SelectedItem is not null;
        CancelButton.IsEnabled = busy;
        SetupButton.IsEnabled = !busy;
        UpdateModeDependentControls();
        if (!busy) UpdateUploadMetadataCounters();
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }

    private void SaveManifestEdits()
    {
        if (_manifest is null || string.IsNullOrWhiteSpace(_manifestPath)) return;
        try
        {
            ClipperJobScript.Save(_manifestPath, _manifest, _clips);
            WriteLog("Perubahan kandidat dan metadata disimpan ke script Gemini.");
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
