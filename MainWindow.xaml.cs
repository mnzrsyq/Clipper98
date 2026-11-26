using System.Diagnostics;
using System.Net.Http;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
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
    private readonly ObservableCollection<YouTubeUploadItem> _uploadItems = [];
    private readonly YouTubeUploadService _youtubeUploader = new();
    private YouTubeService? _youtubeService;
    private CancellationTokenSource? _youtubeCts;
    private ClipCandidate? _metadataEditingClip;
    private YouTubeUploadItem? _selectedUploadItem;
    private CancellationTokenSource? _thumbnailCts;
    private bool _windowReady;
    private bool _updatingClipControls;
    private bool _settingManifestMode;
    private bool _loadingQueueMetadata;
    private bool _updatingSelection;

    public MainWindow()
    {
        _settings.YouTubeUploadQueue ??= [];
        ThemeManager.Apply(_settings.NightMode);
        InitializeComponent();
        foreach (var item in _settings.YouTubeUploadQueue)
        {
            item.PropertyChanged += UploadItem_PropertyChanged;
            _uploadItems.Add(item);
        }
        UploadScheduleGrid.ItemsSource = _uploadItems;
        RefreshQueueNumbers();
        PublishDatePicker.SelectedDate = GetWibNow().Date;
        if (_uploadItems.Count > 0) UploadScheduleGrid.SelectedIndex = 0;
        UpdateUploadQueueSummary();
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
        ShortClipSubtitleCheckBox.IsChecked = _settings.UseShortClipSubtitles;
        LoadSavedSubtitlePresets();
        FramePathText.Text = string.IsNullOrWhiteSpace(_settings.FramePngPath) ? "No frame selected" : _settings.FramePngPath;
        Loaded += (_, _) =>
        {
            _windowReady = true;
            SaveTtsSettings();
            LoadFramePreview();
            UpdatePreview();
        };
        WriteLog("Clipper 98 is ready.");
    }

    private void CopyPromptButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (!IsYouTubeUrl(url))
        {
            SetStatus("STATUS: Enter a YouTube URL first.", true);
            return;
        }

        var prompt = BuildSkillPrompt(url);
        System.Windows.Clipboard.SetText(prompt);
        SetStatus(IsCondensedMode
            ? "STATUS: Condense Video prompt copied. Open Gemini, start a new chat, select the /clipper-ringkas skill, and paste the prompt."
            : "STATUS: Short Clips prompt copied. Open Gemini, start a new chat, select the /clipper skill, and paste the prompt.");
        WriteLog(IsCondensedMode ? "Condense Video prompt copied for Gemini." : "Short Clips prompt copied for Gemini.");
    }

    private string BuildSkillPrompt(string url)
    {
        if (IsCondensedMode)
        {
            var seconds = int.TryParse(SelectedText(TargetDurationBox, "0"), out var value) ? value : 0;
            var narration = CondensedNarrationCheckBox.IsChecked == true ? "on" : "off";
            return seconds == 0
                ? $"{url}\ntarget_duration_seconds=0 narration={narration}\nRingkas video ini menjadi satu video utuh lebih pendek dengan membuang bagian yang tidak perlu tanpa mempercepat. Jaga alur visual dan audio. Buat narasi VO yang natural hanya jika diminta, lalu sertakan judul dan deskripsi YouTube."
                : $"{url}\ntarget_duration_seconds={seconds} narration={narration}\nRingkas video ini menjadi satu video utuh sekitar {seconds / 60} menit tanpa mempercepat. Jika target merusak alur, pertahankan konteks dan sarankan durasi lebih panjang. Buat narasi VO yang natural hanya jika diminta, lalu sertakan judul dan deskripsi YouTube.";
        }

        var requested = int.TryParse(CandidateCountBox.Text, out var count) ? Math.Clamp(count, 1, 20) : 5;
        CandidateCountBox.Text = requested.ToString();
        return $"{url}\nrequested_candidate_count={requested}\nPilih {requested} kandidat klip pendek yang mandiri. Untuk setiap kandidat, sertakan judul YouTube maksimal 100 karakter, deskripsi siap tempel, dan maksimal 3 hashtag relevan.";
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
                ClipDetail.Text = "Mode changed. Copy the prompt for this mode, then import the manifest from Gemini.";
                RenderButton.IsEnabled = false;
            }
        }
        var condensed = IsCondensedMode;
        ShortClipOptionsPanel.Visibility = condensed ? Visibility.Collapsed : Visibility.Visible;
        CondensedOptionsPanel.Visibility = condensed ? Visibility.Visible : Visibility.Collapsed;
        TimestampEditPanel.Visibility = condensed ? Visibility.Collapsed : Visibility.Visible;
        ClipListGroupBox.Header = condensed ? "CONDENSED VIDEO" : "CLIP CANDIDATES";
        UpdateModeDependentControls();
        UpdatePreview();
    }

    private void CondensedSubtitle_Changed(object sender, RoutedEventArgs e)
    {
        if (LanguageBox is null) return;
        UpdateModeDependentControls();
        UpdatePreview();
    }

    private void ShortClipSubtitle_Changed(object sender, RoutedEventArgs e)
    {
        if (LanguageBox is null) return;
        _settings.UseShortClipSubtitles = ShortClipSubtitleCheckBox.IsChecked == true;
        if (_windowReady) SettingsStore.Save(_settings);
        UpdateModeDependentControls();
        UpdatePreview();
    }

    private void CondensedAspect_Changed(object sender, SelectionChangedEventArgs e) => UpdateModeDependentControls();

    private void UpdateModeDependentControls()
    {
        if (SubtitleSettingsPanel is null) return;
        var condensed = IsCondensedMode;
        var useSubtitles = condensed
            ? CondensedSubtitleCheckBox.IsChecked == true
            : ShortClipSubtitleCheckBox.IsChecked == true;
        ShortClipSubtitleCheckBox.Visibility = condensed ? Visibility.Collapsed : Visibility.Visible;
        ShortClipSubtitleCheckBox.IsEnabled = _cts is null && !condensed;
        CondensedSubtitleCheckBox.Visibility = condensed ? Visibility.Visible : Visibility.Collapsed;
        CondensedSubtitleCheckBox.IsEnabled = _cts is null && condensed;
        SubtitleSettingsPanel.IsEnabled = _cts is null && useSubtitles;
        SubtitleSourceBox.IsEnabled = _cts is null && !condensed;
        LanguageBox.IsEnabled = _cts is null && useSubtitles;
        var portraitOutput = condensed && SelectedText(CondensedAspectBox, "source") == "portrait";
        FramingHeaderText.Text = condensed ? "CONDENSED VIDEO FRAMING" : "SELECTED CLIP FRAMING";
        SmartCropBox.ToolTip = portraitOutput
            ? "On: the main 4:5 area follows the face. Off: the 4:5 area stays centered. Output remains 9:16 with a blurred background."
            : "On: the 9:16 crop follows the main face. Off: show the full video frame over a blurred background.";
        SmartCropHelpText.Text = portraitOutput
            ? "The video uses a centered 4:5 crop inside the 9:16 canvas, with a blurred background above and below. If no face is detected, the full image fills the 4:5 area."
            : "If Smart Crop cannot detect a face for at least two seconds, the preview temporarily shows the full video over a blurred background.";
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
            Title = "Select a Clipper manifest from Gemini",
            Filter = "Clipper Python manifest (*.py)|*.py|All files (*.*)|*.*",
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
                Title = string.IsNullOrWhiteSpace(loaded.Manifest.Source.Title) ? "YouTube video" : loaded.Manifest.Source.Title,
                Duration = loaded.Manifest.Source.DurationSeconds ?? 0,
                TranscriptSource = "Gemini"
            };
            ClipList.ItemsSource = _clips;
            if (_clips.Count > 0) ClipList.SelectedIndex = 0;
            WorkProgress.IsIndeterminate = false;
            WorkProgress.Value = 100;
            SetStatus(loaded.Manifest.Mode.Equals("condense", StringComparison.OrdinalIgnoreCase)
                ? $"STATUS: condensed video loaded; {_clips[0].KeptSegments.Count} segments will be joined."
                : $"STATUS: {_clips.Count} candidates loaded from Gemini.");
            ClipDetail.Text = loaded.Manifest.Mode.Equals("condense", StringComparison.OrdinalIgnoreCase)
                ? BuildCondensedDetail(_clips[0])
                : $"File: {Path.GetFileName(dialog.FileName)}\nTitle: {_scan.Title}\nTimestamp edits made here will be saved to the manifest.";
            WriteLog($"Gemini manifest loaded: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            SetStatus("STATUS: The Gemini manifest could not be loaded.", true);
            WriteLog("ERROR: " + ex.Message);
            WpfMessageBox.Show(ex.Message, "Import Gemini manifest", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            SetStatus("STATUS: Select at least one candidate before creating clips.", true);
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
            var useSubtitles = isCondensed
                ? CondensedSubtitleCheckBox.IsChecked == true
                : ShortClipSubtitleCheckBox.IsChecked == true;
            var useNarration = isCondensed && CondensedNarrationCheckBox.IsChecked == true;
            if (useNarration && selected.Any(clip => clip.KeptSegments.Count == 0
                    || clip.KeptSegments.Any(segment => string.IsNullOrWhiteSpace(segment.Narration))))
                throw new InvalidOperationException("The manifest does not include narration for every segment. Copy the Gemini prompt again with narration enabled, then import the updated manifest.");
            var subtitleSource = isCondensed ? "whisper" : SelectedText(SubtitleSourceBox, "auto");
            var preserveSourceAspect = isCondensed && SelectedText(CondensedAspectBox, "source") == "source";
            var useFourByFiveFrame = isCondensed && !preserveSourceAspect;
            var framePath = isCondensed ? string.Empty : _settings.FramePngPath;
            var ttsOptions = ReadTtsOptions();
            if (!string.IsNullOrWhiteSpace(framePath) && !File.Exists(framePath))
                throw new FileNotFoundException("The selected PNG frame could not be found. Select it again or clear it from the panel.", framePath);
            var outputFolder = OutputBox.Text;
            WorkProgress.IsIndeterminate = false; WorkProgress.Value = 0;
            var batchStarted = DateTime.Now;
            for (var i = 0; i < selected.Count; i++)
            {
                var index = i; // salinan lokal: callback progres berjalan async dan tidak boleh membaca variabel loop
                var clip = selected[index];
                SetStatus(index == 0
                    ? $"STATUS: Rendering clip 1/{selected.Count}: {clip.Name}. Batch estimates appear after the first clip."
                    : $"STATUS: Rendering clip {index + 1}/{selected.Count}: {clip.Name}. Estimated batch time remaining: {TimeSpan.FromSeconds((DateTime.Now - batchStarted).TotalSeconds / index * (selected.Count - index)):mm\\:ss}.");
                WorkProgress.IsIndeterminate = true;
                var progress = new Progress<JobProgress>(p => OnRenderProgress(p, index, selected.Count));
                var file = await RenderViaCliAsync(url, scan, clip, ReadStyle(), outputFolder, language, subtitleSource,
                    framePath, useSubtitles, preserveSourceAspect, useFourByFiveFrame, useNarration, ttsOptions, progress, cts.Token);
                WorkProgress.IsIndeterminate = false;
                WorkProgress.Value = (index + 1) * 100.0 / selected.Count;
                AddRenderedClipToUploadQueue(file, clip);
                WriteLog("Completed: " + file);
            }
            SetStatus(isCondensed
                ? "STATUS: Condensed video created and added to the YouTube queue."
                : $"STATUS: Created {selected.Count} clips and added them to the YouTube queue.");
            WpfMessageBox.Show(isCondensed
                ? $"Condensed video created.\n\n{OutputBox.Text}"
                : $"Created {selected.Count} clips.\n\n{OutputBox.Text}", "Clipper 98", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            SetStatus("STATUS: Rendering canceled.", true);
            WriteLog("Rendering canceled.");
        }
        catch (Exception ex)
        {
            SetStatus("STATUS: Rendering failed.", true);
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
            if (!string.IsNullOrWhiteSpace(p.Eta)) parts.Add("remaining " + p.Eta);

            if (p.Percent is { } value)
            {
                WorkProgress.IsIndeterminate = false;
                WorkProgress.Value = Math.Clamp((index + value / 100.0) / total * 100.0, 0, 100);
            }
            else
            {
                WorkProgress.IsIndeterminate = true; // percentage unavailable; keep the estimate indeterminate
            }
            var transfer = _manifest?.Mode.Equals("condense", StringComparison.OrdinalIgnoreCase) == true
                ? p.Message
                : $"Downloading clip {index + 1}/{total}";
            SetStatus($"STATUS: {transfer}: {(parts.Count > 0 ? string.Join(" · ", parts) : "in progress")}");
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
            throw new InvalidOperationException("Clipper98.exe was not found in the application folder.");

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
                throw new InvalidOperationException(errorMessage ?? result.Tail() ?? "Rendering failed.");
            if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
                throw new InvalidOperationException("Rendering finished, but the output file could not be found.");
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
        SetStatus("STATUS: Canceling...");
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
            : $"{clip.TimeRange}\n{clip.Hook}\n{clip.Reason}" + (string.IsNullOrWhiteSpace(clip.Caption) ? "" : $"\nCaption: {clip.Caption}\n{clip.Hashtags}") + "\nThe clip uses these exact timestamps.";
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
            ? $"\nTarget: about {FormatDuration(_manifest.TargetDurationSeconds)} (estimated; preserve the complete sequence)"
            : string.Empty;
        var parts = clip.KeptSegments.Select((segment, index) =>
            $"{index + 1}. {segment.TimeRange}: {segment.Reason}");
        return $"{clip.KeptSegments.Count} segments retained · output about {FormatDuration(keptSeconds)}{target}\n\n{string.Join("\n", parts)}";
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
            ? $"{clip.Name}: full frame retained so objects and slides remain visible."
            : $"{clip.Name}: smart crop follows the main face.");
        UpdatePreview();
    }

    private void AddTimestampButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scan is null) { SetStatus("STATUS: Scan a video first.", true); return; }
        if (_clips.Count >= 20) { SetStatus("STATUS: The limit of 20 candidates has been reached.", true); return; }
        var start = 0d;
        foreach (var existing in _clips.OrderBy(clip => clip.Start))
            if (start < existing.End && start + 30 > existing.Start) start = existing.End;
        var remaining = _scan.Duration > 0 ? _scan.Duration - start : 30;
        var duration = Math.Min(30, remaining);
        if (duration <= 0)
        { SetStatus("STATUS: No unused range is available for another timestamp.", true); return; }
        var index = _clips.Count + 1;
        var clip = new ClipCandidate { Name = $"custom_{index:00}", Start = start, End = start + duration, Hook = "Custom timestamp", Reason = "Time range set manually." };
        _clips.Add(clip); ClipList.Items.Refresh(); ClipList.SelectedItem = clip;
        SaveManifestEdits();
    }

    private void UpdateTimestampButton_Click(object sender, RoutedEventArgs e)
    {
        if (ClipList.SelectedItem is not ClipCandidate clip || _scan is null) { SetStatus("STATUS: Select a candidate to edit its timestamps.", true); return; }
        if (!TimeSpan.TryParse(StartTimeBox.Text, out var start) || !TimeSpan.TryParse(EndTimeBox.Text, out var end) || start.TotalSeconds < 0 || end <= start || (_scan.Duration > 0 && end.TotalSeconds > _scan.Duration))
        { SetStatus("STATUS: Enter valid HH:MM:SS timestamps. The start must come before the end and both must fall within the video.", true); return; }
        if (_clips.Any(other => !ReferenceEquals(other, clip) && start.TotalSeconds < other.End && other.Start < end.TotalSeconds))
        { SetStatus("STATUS: These timestamps overlap another candidate.", true); return; }
        clip.Start = start.TotalSeconds; clip.End = end.TotalSeconds;
        ClipList.Items.Refresh();
        SaveManifestEdits();
        SetStatus("STATUS: Timestamps updated.");
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
        YouTubeTitleCountText.Foreground = titleLength > 100
            ? (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["ErrorBrush"]
            : (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["MutedTextBrush"];
        YouTubeDescriptionCountText.Text = $"{descriptionLength} / 5000";
        YouTubeDescriptionCountText.Foreground = descriptionLength > 5000
            ? (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["ErrorBrush"]
            : (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["MutedTextBrush"];
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
            SetStatus("STATUS: The title must contain 1 to 100 characters.", true);
            return;
        }
        System.Windows.Clipboard.SetText(title);
        SetStatus($"STATUS: Title copied ({length}/100 characters).");
    }

    private void CopyYouTubeDescriptionButton_Click(object sender, RoutedEventArgs e)
    {
        CommitUploadMetadata();
        var description = YouTubeDescriptionBox.Text.Trim();
        var length = new System.Globalization.StringInfo(description).LengthInTextElements;
        if (length is 0 or > 5000)
        {
            SetStatus("STATUS: The description must contain no more than 5,000 characters.", true);
            return;
        }
        System.Windows.Clipboard.SetText(description);
        SetStatus($"STATUS: Description and hashtags copied ({length}/5,000 characters).");
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select the output folder",
            SelectedPath = Directory.Exists(OutputBox.Text) ? OutputBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        OutputBox.Text = dialog.SelectedPath;
        _settings.OutputFolder = dialog.SelectedPath;
        SettingsStore.Save(_settings);
        SetStatus("STATUS: Output folder saved.");
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(OutputBox.Text);
        Process.Start(new ProcessStartInfo { FileName = OutputBox.Text, UseShellExecute = true });
    }

    private void WorkspaceMode_Click(object sender, RoutedEventArgs e)
    {
        SetWorkspaceMode(ReferenceEquals(sender, YouTubeModeButton));
    }

    private void SetWorkspaceMode(bool youtubeMode)
    {
        EditorModeButton.IsChecked = !youtubeMode;
        YouTubeModeButton.IsChecked = youtubeMode;
        EditorSourcePanel.Visibility = youtubeMode ? Visibility.Collapsed : Visibility.Visible;
        EditorOptionsPanel.Visibility = youtubeMode ? Visibility.Collapsed : Visibility.Visible;
        ClipListGroupBox.Visibility = youtubeMode ? Visibility.Collapsed : Visibility.Visible;
        EditorClipDetailsPanel.Visibility = youtubeMode ? Visibility.Collapsed : Visibility.Visible;
        EditorPreviewGroup.Visibility = youtubeMode ? Visibility.Collapsed : Visibility.Visible;
        EditorStyleGroup.Visibility = youtubeMode ? Visibility.Collapsed : Visibility.Visible;
        YouTubeWorkspacePanel.Visibility = youtubeMode ? Visibility.Visible : Visibility.Collapsed;
        EditorFooter.Visibility = youtubeMode ? Visibility.Collapsed : Visibility.Visible;
        UploadFooter.Visibility = youtubeMode ? Visibility.Visible : Visibility.Collapsed;
        if (youtubeMode)
        {
            UploadScheduleGrid.Items.Refresh();
            UpdateUploadQueueSummary();
            SetStatus(_uploadItems.Count == 0
                ? "STATUS: Finished renders will appear in the publication queue."
                : "STATUS: Select videos to schedule. Finished renders are added automatically.");
        }
    }

    private void AddRenderedClipToUploadQueue(string filePath, ClipCandidate clip)
    {
        var fullPath = Path.GetFullPath(filePath);
        var item = _uploadItems.FirstOrDefault(existing => !string.IsNullOrWhiteSpace(existing.FilePath)
            && string.Equals(Path.GetFullPath(existing.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            item = new YouTubeUploadItem
            {
                FilePath = fullPath,
                ClipName = Path.GetFileName(fullPath),
                Title = clip.YouTubeTitle,
                Description = clip.YouTubeDescription,
                IsSelected = true
            };
            item.PropertyChanged += UploadItem_PropertyChanged;
            _uploadItems.Add(item);
        }
        else
        {
            item.IsSelected = true;
            item.Title = clip.YouTubeTitle;
            item.Description = clip.YouTubeDescription;
        }

        UploadScheduleGrid.SelectedItem = item;
        RefreshQueueNumbers();
        UploadScheduleGrid.Items.Refresh();
        PersistUploadQueue();
        UpdateUploadQueueSummary();
        WriteLog("Finished render added to the YouTube queue: " + Path.GetFileName(fullPath));
    }

    private void AddVideosToUploadQueue_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "Add videos to the publication queue",
            Filter = "Video MP4 (*.mp4)|*.mp4",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        var added = 0;
        YouTubeUploadItem? lastAdded = null;
        foreach (var path in dialog.FileNames)
        {
            var fullPath = Path.GetFullPath(path);
            if (_uploadItems.Any(item => !string.IsNullOrWhiteSpace(item.FilePath)
                && string.Equals(Path.GetFullPath(item.FilePath), fullPath, StringComparison.OrdinalIgnoreCase)))
                continue;

            lastAdded = new YouTubeUploadItem
            {
                FilePath = fullPath,
                ClipName = Path.GetFileName(fullPath),
                Title = Path.GetFileNameWithoutExtension(fullPath),
                IsSelected = true
            };
            lastAdded.PropertyChanged += UploadItem_PropertyChanged;
            _uploadItems.Add(lastAdded);
            added++;
        }

        if (added == 0)
        {
            SetStatus("STATUS: Every selected video is already in the queue.");
            return;
        }

        RefreshQueueNumbers();
        UploadScheduleGrid.SelectedItem = lastAdded;
        PersistUploadQueue();
        UploadScheduleGrid.Items.Refresh();
        UpdateUploadQueueSummary();
        SetStatus($"STATUS: Added {added} video{(added == 1 ? "" : "s")} to the publication queue.");
    }

    private void ImportUploadMetadata_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "Import metadata from a Clipper Python file",
            Filter = "Clipper Python manifest (*.py)|*.py|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var loaded = ClipperJobScript.Load(dialog.FileName);
            var outputFolder = OutputBox.Text;
            var outputFiles = Directory.Exists(outputFolder)
                ? Directory.EnumerateFiles(outputFolder, "*.mp4", SearchOption.TopDirectoryOnly).ToList()
                : [];
            outputFiles.AddRange(_uploadItems.Where(item => File.Exists(item.FilePath)).Select(item => item.FilePath));
            var fileByName = outputFiles
                .GroupBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var matched = 0;
            var unmatched = new List<string>();

            foreach (var clip in loaded.Clips)
            {
                var outputName = Path.GetFileNameWithoutExtension(ClipPipeline.BuildFileName(loaded.Manifest.Source.Title, clip));
                if (!fileByName.TryGetValue(outputName, out var videoPath))
                {
                    unmatched.Add(clip.Name);
                    continue;
                }

                var fullPath = Path.GetFullPath(videoPath);
                var item = _uploadItems.FirstOrDefault(existing =>
                    string.Equals(Path.GetFullPath(existing.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));
                if (item is null)
                {
                    item = new YouTubeUploadItem
                    {
                        FilePath = fullPath,
                        ClipName = Path.GetFileName(fullPath),
                        IsSelected = true
                    };
                    item.PropertyChanged += UploadItem_PropertyChanged;
                    _uploadItems.Add(item);
                }

                var title = string.IsNullOrWhiteSpace(clip.YouTubeTitle) ? loaded.Manifest.YouTubeTitle : clip.YouTubeTitle;
                var description = string.IsNullOrWhiteSpace(clip.YouTubeDescription) ? loaded.Manifest.YouTubeDescription : clip.YouTubeDescription;
                if (!string.IsNullOrWhiteSpace(title)) item.Title = title;
                if (!string.IsNullOrWhiteSpace(description)) item.Description = description;
                item.IsSelected = true;
                matched++;
            }

            RefreshQueueNumbers();
            PersistUploadQueue();
            UploadScheduleGrid.Items.Refresh();
            UpdateUploadQueueSummary();
            if (matched > 0)
            {
                _selectedUploadItem = _uploadItems.LastOrDefault(item => item.IsSelected);
                UploadScheduleGrid.SelectedItem = _selectedUploadItem;
            }

            var message = matched == 0
                ? "No rendered video matched this manifest. Add the original MP4 files or place them in the configured output folder, then import the manifest again."
                : $"Imported metadata for {matched} video{(matched == 1 ? "" : "s")}.";
            if (unmatched.Count > 0 && matched > 0)
                message += $" {unmatched.Count} manifest item{(unmatched.Count == 1 ? " was" : "s were")} not matched by filename.";
            SetStatus("STATUS: " + message, matched == 0);
            if (matched == 0 || unmatched.Count > 0)
                WpfMessageBox.Show(this, message, "Metadata import", MessageBoxButton.OK,
                    matched == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            SetStatus("STATUS: Metadata could not be imported.", true);
            WriteLog("ERROR: " + ex.Message);
            WpfMessageBox.Show(this, ex.Message, "Metadata import", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UploadScheduleGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitUploadQueueMetadata();
        PersistUploadQueue();
        _selectedUploadItem = UploadScheduleGrid.SelectedItem as YouTubeUploadItem;
        _loadingQueueMetadata = true;
        QueueTitleBox.Text = _selectedUploadItem?.Title ?? string.Empty;
        QueueDescriptionBox.Text = _selectedUploadItem?.Description ?? string.Empty;
        if (_selectedUploadItem?.PublishAt is { } scheduled)
        {
            var local = TimeZoneInfo.ConvertTime(scheduled, GetWibTimeZone());
            PublishDatePicker.SelectedDate = local.Date;
            PublishTimeBox.Text = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        else
        {
            PublishDatePicker.SelectedDate = GetWibNow().Date;
            PublishTimeBox.Text = "19:30";
        }
        var canEdit = _youtubeCts is null
            && _selectedUploadItem is not null
            && string.IsNullOrWhiteSpace(_selectedUploadItem.YouTubeVideoId);
        QueueTitleBox.IsEnabled = canEdit;
        QueueDescriptionBox.IsEnabled = canEdit;
        PublishDatePicker.IsEnabled = canEdit;
        PublishTimeBox.IsEnabled = canEdit;
        _loadingQueueMetadata = false;
        RemoveUploadQueueItemButton.IsEnabled = UploadScheduleGrid.SelectedItems.Count > 0 && _youtubeCts is null;
        UpdateQueueActionButtons();
    }

    private void SelectAllUpload_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingSelection) return;
        _updatingSelection = true;
        var select = _uploadItems.Any(item => !item.IsSelected);
        foreach (var item in _uploadItems) item.IsSelected = select;
        _updatingSelection = false;
        PersistUploadQueue();
        UpdateUploadQueueSummary();
    }

    private void UploadItem_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(YouTubeUploadItem.IsSelected) || _updatingSelection) return;
        PersistUploadQueue();
        UpdateUploadQueueSummary();
    }

    private void MoveQueueUp_Click(object sender, RoutedEventArgs e) => MoveSelectedQueueRows(up: true);

    private void MoveQueueDown_Click(object sender, RoutedEventArgs e) => MoveSelectedQueueRows(up: false);

    private void MoveSelectedQueueRows(bool up)
    {
        var selected = UploadScheduleGrid.SelectedItems.Cast<YouTubeUploadItem>().ToHashSet();
        if (selected.Count == 0) return;

        if (up)
        {
            for (var index = 1; index < _uploadItems.Count; index++)
            {
                if (selected.Contains(_uploadItems[index]) && !selected.Contains(_uploadItems[index - 1]))
                    _uploadItems.Move(index, index - 1);
            }
        }
        else
        {
            for (var index = _uploadItems.Count - 2; index >= 0; index--)
            {
                if (selected.Contains(_uploadItems[index]) && !selected.Contains(_uploadItems[index + 1]))
                    _uploadItems.Move(index, index + 1);
            }
        }

        UploadScheduleGrid.SelectedItems.Clear();
        foreach (var item in selected) UploadScheduleGrid.SelectedItems.Add(item);
        RefreshQueueNumbers();
        PersistUploadQueue();
        UpdateUploadQueueSummary();
    }

    private async void YouTubeConnection_Click(object sender, RoutedEventArgs e)
    {
        if (_youtubeService is not null)
        {
            var choice = WpfMessageBox.Show(this,
                "Disconnect the YouTube account on this PC? Clipper will remove its saved sign-in token.",
                "Disconnect YouTube", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes) return;

            _youtubeService.Dispose();
            _youtubeService = null;
            await new ProtectedYouTubeTokenStore().ClearAsync();
            UpdateQueueActionButtons();
            SetStatus("STATUS: YouTube account disconnected on this PC.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.YouTubeClientSecretsJson))
        {
            var dialog = new WpfOpenFileDialog
            {
                Title = "Select a Google OAuth Desktop client JSON file",
                Filter = "JSON credentials (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                var clientJson = File.ReadAllText(dialog.FileName);
                ValidateYouTubeClientSecrets(clientJson);
                _settings.YouTubeClientSecretsJson = clientJson;
                SettingsStore.Save(_settings);
            }
            catch (Exception ex)
            {
                SetStatus("STATUS: The selected OAuth file is not a valid Desktop client file.", true);
                WpfMessageBox.Show(this, ex.Message, "YouTube setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        _youtubeCts = new CancellationTokenSource();
        YouTubeConnectionButton.IsEnabled = false;
        UploadDisabledButton.IsEnabled = false;
        SetStatus("STATUS: Opening Google sign-in...");
        try
        {
            _youtubeService = await _youtubeUploader.ConnectAsync(
                _settings.YouTubeClientSecretsJson,
                _youtubeCts.Token);
            SetStatus("STATUS: YouTube account connected on this PC.");
            WriteLog("YouTube OAuth authorization completed.");
        }
        catch (Exception ex)
        {
            SetStatus("STATUS: YouTube could not connect. Check the OAuth setup and try again.", true);
            WriteLog("YouTube connection failed: " + ex.Message);
            WpfMessageBox.Show(this, ex.Message, "YouTube connection", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _youtubeCts?.Dispose();
            _youtubeCts = null;
            UpdateQueueActionButtons();
        }
    }

    private static void ValidateYouTubeClientSecrets(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("installed", out var installed)
            || !installed.TryGetProperty("client_id", out _)
            || !installed.TryGetProperty("client_secret", out _))
            throw new InvalidOperationException(
                "Choose the JSON file for an OAuth client with application type Desktop app. Enable YouTube Data API v3 in the same Google Cloud project first.");
    }

    private async void UploadSelectedVideos_Click(object sender, RoutedEventArgs e)
    {
        CommitUploadQueueMetadata();
        var selected = _uploadItems.Where(item => item.IsSelected).ToList();
        if (_youtubeService is null)
        {
            SetStatus("STATUS: Connect a YouTube channel before uploading.", true);
            return;
        }
        if (selected.Count == 0)
        {
            SetStatus("STATUS: Check the videos you want to upload.", true);
            return;
        }

        var invalid = selected.FirstOrDefault(item =>
            !File.Exists(item.FilePath)
            || string.IsNullOrWhiteSpace(item.Title)
            || item.Title.Length > 100
            || item.Description.Length > 5000
            || item.PublishAt is not { } publishAt
            || publishAt <= DateTimeOffset.UtcNow
            || !string.IsNullOrWhiteSpace(item.YouTubeVideoId));
        var uncertain = selected.FirstOrDefault(item => item.UploadStatus == "Check YouTube Studio");
        if (uncertain is not null)
        {
            var uncertainItems = selected.Where(item => item.UploadStatus == "Check YouTube Studio").ToList();
            var names = string.Join(Environment.NewLine, uncertainItems.Take(3).Select(item => item.FileName));
            if (uncertainItems.Count > 3) names += Environment.NewLine + $"and {uncertainItems.Count - 3} more";
            var retryChoice = WpfMessageBox.Show(this,
                $"Confirm that these videos do not appear in YouTube Studio before retrying. Retrying a video that is already there can create a duplicate.{Environment.NewLine}{Environment.NewLine}{names}{Environment.NewLine}{Environment.NewLine}Retry the selected videos?",
                "Confirm YouTube upload retry", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (retryChoice != MessageBoxResult.Yes) return;
            foreach (var item in uncertainItems) item.UploadStatus = "Queued";
        }
        if (invalid is not null)
        {
            SetStatus($"STATUS: Check the title, description, local file, and future publish time for {invalid.FileName}.", true);
            return;
        }

        var choice = WpfMessageBox.Show(this,
            $"Upload {selected.Count} selected video{(selected.Count == 1 ? "" : "s")} now? YouTube will keep each video Private and publish it at its scheduled WIB time.",
            "Upload scheduled videos", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (choice != MessageBoxResult.Yes) return;

        _youtubeCts = new CancellationTokenSource();
        UpdateQueueActionButtons();
        WorkProgress.IsIndeterminate = false;
        WorkProgress.Value = 0;
        var failures = 0;
        try
        {
            for (var index = 0; index < selected.Count; index++)
            {
                _youtubeCts.Token.ThrowIfCancellationRequested();
                var item = selected[index];
                item.UploadStatus = "Uploading";
                PersistUploadQueue();
                var totalBytes = new FileInfo(item.FilePath).Length;
                var progress = new Progress<IUploadProgress>(update =>
                {
                    var percent = totalBytes > 0 ? Math.Clamp(update.BytesSent * 100.0 / totalBytes, 0, 100) : 0;
                    WorkProgress.IsIndeterminate = false;
                    WorkProgress.Value = (index + percent / 100.0) * 100.0 / selected.Count;
                    SetStatus($"STATUS: Uploading {index + 1}/{selected.Count}: {item.FileName} ({percent:0}%).");
                });

                try
                {
                    var uploaded = await _youtubeUploader.UploadScheduledAsync(
                        _youtubeService,
                        item,
                        progress,
                        _youtubeCts.Token);
                    item.YouTubeVideoId = uploaded.Id ?? string.Empty;
                    item.UploadStatus = "Scheduled on YouTube";
                    item.IsSelected = false;
                    WriteLog($"Scheduled on YouTube: {item.FileName} (https://youtu.be/{uploaded.Id})");
                }
                catch (OperationCanceledException) when (_youtubeCts.IsCancellationRequested)
                {
                    item.UploadStatus = "Upload interrupted";
                    PersistUploadQueue();
                    throw;
                }
                catch (YouTubeUploadOutcomeUnknownException ex)
                {
                    failures++;
                    item.UploadStatus = "Check YouTube Studio";
                    item.IsSelected = false;
                    WriteLog($"Upload outcome needs review for {item.FileName}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    failures++;
                    item.UploadStatus = "Upload failed";
                    WriteLog($"Upload failed for {item.FileName}: {ex.Message}");
                }

                PersistUploadQueue();
                UploadScheduleGrid.Items.Refresh();
                UpdateUploadQueueSummary();
                WorkProgress.Value = (index + 1) * 100.0 / selected.Count;
            }

            SetStatus(failures == 0
                ? $"STATUS: {selected.Count} video{(selected.Count == 1 ? "" : "s")} uploaded and scheduled on YouTube."
                : $"STATUS: Upload finished with {failures} error{(failures == 1 ? "" : "s")}. Review the YouTube status column and process log.", failures > 0);
        }
        catch (OperationCanceledException)
        {
            SetStatus("STATUS: Upload was interrupted. Check YouTube Studio before retrying to avoid a duplicate upload.", true);
        }
        finally
        {
            _youtubeCts?.Dispose();
            _youtubeCts = null;
            WorkProgress.IsIndeterminate = false;
            UpdateQueueActionButtons();
        }
    }

    private void QueueMetadata_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loadingQueueMetadata || _selectedUploadItem is null) return;
        _selectedUploadItem.Title = QueueTitleBox.Text;
        _selectedUploadItem.Description = QueueDescriptionBox.Text;
    }

    private void QueueMetadata_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitUploadQueueMetadata();
        PersistUploadQueue();
    }

    private void CommitUploadQueueMetadata()
    {
        if (_selectedUploadItem is null || _loadingQueueMetadata) return;
        _selectedUploadItem.Title = QueueTitleBox.Text.Trim();
        _selectedUploadItem.Description = QueueDescriptionBox.Text.Trim();
    }

    private void UploadSelection_Click(object sender, RoutedEventArgs e)
    {
        PersistUploadQueue();
        UpdateUploadQueueSummary();
    }

    private void AutoScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        CommitUploadQueueMetadata();
        var waiting = _uploadItems.Where(item => item.IsSelected
            && item.PublishAt is null
            && string.IsNullOrWhiteSpace(item.YouTubeVideoId)
            && File.Exists(item.FilePath)).ToList();
        if (waiting.Count == 0)
        {
            var selectedCount = _uploadItems.Count(item => item.IsSelected);
            SetStatus(selectedCount == 0
                ? "STATUS: Select the videos you want to schedule."
                : "STATUS: Selected videos already have publish times, are on YouTube, or have missing files.", selectedCount == 0);
            return;
        }

        var occupied = _uploadItems.Where(item => item.PublishAt.HasValue)
            .Select(item => item.PublishAt!.Value).ToHashSet();
        var cursor = GetWibNow();
        try
        {
            foreach (var item in waiting)
            {
                var slot = FindNextScheduleSlot(cursor, occupied);
                item.PublishAt = slot;
                occupied.Add(slot);
                cursor = slot;
            }
        }
        catch (InvalidOperationException ex)
        {
            SetStatus("STATUS: " + ex.Message, true);
            return;
        }

        PersistUploadQueue();
        UploadScheduleGrid.Items.Refresh();
        UpdateUploadQueueSummary();
        SetStatus($"STATUS: Assigned WIB publish times to {waiting.Count} video{(waiting.Count == 1 ? "" : "s")}.");
        WriteLog($"Auto-Schedule assigned {waiting.Count} WIB slot{(waiting.Count == 1 ? "" : "s")} in the local Clipper98 queue.");
    }

    private void SetPublishTime_Click(object sender, RoutedEventArgs e)
    {
        CommitUploadQueueMetadata();
        if (_selectedUploadItem is null || !_selectedUploadItem.IsSelected)
        {
            SetStatus("STATUS: Select and check one video before setting its publish time.", true);
            return;
        }
        if (!string.IsNullOrWhiteSpace(_selectedUploadItem.YouTubeVideoId))
        {
            SetStatus("STATUS: This video is already scheduled on YouTube. Change its publish time in YouTube Studio.", true);
            return;
        }
        if (!File.Exists(_selectedUploadItem.FilePath))
        {
            SetStatus("STATUS: The local video file could not be found.", true);
            return;
        }
        if (PublishDatePicker.SelectedDate is not { } date
            || !TimeOnly.TryParse(PublishTimeBox.Text, CultureInfo.InvariantCulture, out var time))
        {
            SetStatus("STATUS: Enter a valid WIB date and 24-hour time, such as 19:30.", true);
            return;
        }

        var zone = GetWibTimeZone();
        var wallClock = DateTime.SpecifyKind(date.Date.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
        var scheduled = new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock));
        if (scheduled <= GetWibNow())
        {
            SetStatus("STATUS: The publish time must be in the future.", true);
            return;
        }
        if (_uploadItems.Any(item => !ReferenceEquals(item, _selectedUploadItem)
            && item.PublishAt is { } existing && existing == scheduled))
        {
            SetStatus("STATUS: Another video in this local queue already uses that time.", true);
            return;
        }

        _selectedUploadItem.PublishAt = scheduled;
        PersistUploadQueue();
        UploadScheduleGrid.Items.Refresh();
        UpdateUploadQueueSummary();
        SetStatus($"STATUS: Publish time saved for {scheduled:MMM d, yyyy h:mm tt} WIB.");
    }

    private void ClearPublishTime_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUploadItem is null)
        {
            SetStatus("STATUS: Select a video to clear its publish time.", true);
            return;
        }
        if (!string.IsNullOrWhiteSpace(_selectedUploadItem.YouTubeVideoId))
        {
            SetStatus("STATUS: This video is already scheduled on YouTube. Change its publish time in YouTube Studio.", true);
            return;
        }
        _selectedUploadItem.PublishAt = null;
        PersistUploadQueue();
        UploadScheduleGrid.Items.Refresh();
        UpdateUploadQueueSummary();
        SetStatus("STATUS: Publish time cleared from the local queue.");
    }

    private void RemoveUploadQueueItem_Click(object sender, RoutedEventArgs e)
    {
        var selected = UploadScheduleGrid.SelectedItems.Cast<YouTubeUploadItem>().ToList();
        if (selected.Count == 0)
        {
            SetStatus("STATUS: Select one or more queue rows to remove.", true);
            return;
        }
        foreach (var item in selected) _uploadItems.Remove(item);
        _selectedUploadItem = null;
        QueueTitleBox.Clear();
        QueueDescriptionBox.Clear();
        UploadScheduleGrid.SelectedItem = null;
        RefreshQueueNumbers();
        PersistUploadQueue();
        UpdateUploadQueueSummary();
        SetStatus($"STATUS: Removed {selected.Count} row{(selected.Count == 1 ? "" : "s")} from the queue. Video files remain on disk.");
    }

    private void PersistUploadQueue()
    {
        _settings.YouTubeUploadQueue = _uploadItems.ToList();
        SettingsStore.Save(_settings);
    }

    private void RefreshQueueNumbers()
    {
        for (var index = 0; index < _uploadItems.Count; index++)
            _uploadItems[index].QueueNumber = index + 1;
        UploadScheduleGrid.Items.Refresh();
        UpdateSelectAllState();
    }

    private void UpdateUploadQueueSummary()
    {
        if (UploadQueueSummaryText is null) return;
        var selectedItems = _uploadItems.Where(item => item.IsSelected).ToList();
        var scheduled = selectedItems.Count(item => item.PublishAt.HasValue);
        UploadQueueSummaryText.Text = $"{selectedItems.Count} selected · {scheduled} scheduled · times use WIB";
        UploadEmptyStateText.Visibility = _uploadItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateQueueActionButtons();
    }

    private void UpdateSelectAllState()
    {
        if (SelectAllUploadCheckBox is null) return;
        var selected = _uploadItems.Count(item => item.IsSelected);
        _updatingSelection = true;
        SelectAllUploadCheckBox.IsChecked = selected == 0 ? false : selected == _uploadItems.Count ? true : null;
        _updatingSelection = false;
    }

    private void UpdateQueueActionButtons()
    {
        if (UploadScheduleGrid is null || UploadDisabledButton is null) return;
        var busy = _youtubeCts is not null;
        var selected = _uploadItems.Where(item => item.IsSelected).ToList();
        var ready = selected.Count > 0 && selected.All(item =>
            File.Exists(item.FilePath)
            && !string.IsNullOrWhiteSpace(item.Title)
            && item.Title.Length <= 100
            && item.Description.Length <= 5000
            && item.PublishAt is { } publishAt
            && publishAt > DateTimeOffset.UtcNow
            && string.IsNullOrWhiteSpace(item.YouTubeVideoId));
        UploadDisabledButton.IsEnabled = _youtubeService is not null && ready && !busy;
        UploadDisabledButton.ToolTip = _youtubeService is null
            ? "Connect a YouTube channel to enable scheduled uploads."
            : ready
                ? "Upload selected videos as Private and publish them at their scheduled WIB times."
                : "Select videos with a local file, title, description within limits, and a future publish time.";
        YouTubeConnectionButton.Content = _youtubeService is null ? "CONNECT YOUTUBE" : "DISCONNECT YOUTUBE";
        YouTubeConnectionStatusText.Text = _youtubeService is null
            ? "Not connected. Connect a YouTube channel to upload scheduled videos."
            : "Connected. Selected videos will upload as Private and publish at their scheduled WIB times.";
        AddQueueVideosButton.IsEnabled = !busy;
        ImportQueueMetadataButton.IsEnabled = !busy;
        MoveQueueUpButton.IsEnabled = !busy && UploadScheduleGrid.SelectedItems.Count > 0;
        MoveQueueDownButton.IsEnabled = !busy && UploadScheduleGrid.SelectedItems.Count > 0;
        RemoveUploadQueueItemButton.IsEnabled = !busy && UploadScheduleGrid.SelectedItems.Count > 0;
        SelectAllUploadCheckBox.IsEnabled = !busy;
        UploadScheduleGrid.IsReadOnly = busy;
        var canEditMetadata = !busy && _selectedUploadItem is not null
            && string.IsNullOrWhiteSpace(_selectedUploadItem.YouTubeVideoId);
        QueueTitleBox.IsEnabled = canEditMetadata;
        QueueDescriptionBox.IsEnabled = canEditMetadata;
        PublishDatePicker.IsEnabled = canEditMetadata;
        PublishTimeBox.IsEnabled = canEditMetadata;
        AutoScheduleButton.IsEnabled = !busy;
        SetPublishTimeButton.IsEnabled = !busy && _selectedUploadItem is not null && string.IsNullOrWhiteSpace(_selectedUploadItem.YouTubeVideoId);
        ClearPublishTimeButton.IsEnabled = !busy && _selectedUploadItem is not null && string.IsNullOrWhiteSpace(_selectedUploadItem.YouTubeVideoId);
        YouTubeConnectionButton.IsEnabled = !busy;
    }

    private static DateTimeOffset GetWibNow() => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, GetWibTimeZone());

    private static TimeZoneInfo GetWibTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Local; }
    }

    private static DateTimeOffset FindNextScheduleSlot(DateTimeOffset after, HashSet<DateTimeOffset> occupied)
    {
        var zone = GetWibTimeZone();
        var localStart = TimeZoneInfo.ConvertTime(after, zone).Date;
        for (var dayOffset = 0; dayOffset < 366; dayOffset++)
        {
            var date = localStart.AddDays(dayOffset);
            var slots = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                ? WeekendPublishSlots
                : WeekdayPublishSlots;
            foreach (var slot in slots)
            {
                var wallClock = DateTime.SpecifyKind(date.Add(slot.ToTimeSpan()), DateTimeKind.Unspecified);
                var candidate = new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock));
                if (candidate <= after || occupied.Contains(candidate)) continue;
                return candidate;
            }
        }
        throw new InvalidOperationException("Tidak ditemukan slot publikasi kosong untuk 12 bulan ke depan.");
    }

    private static readonly TimeOnly[] WeekdayPublishSlots =
    [
        new(6, 30), new(9, 30), new(11, 45), new(13, 30),
        new(16, 0), new(18, 0), new(19, 30), new(21, 15)
    ];

    private static readonly TimeOnly[] WeekendPublishSlots =
    [
        new(8, 0), new(10, 0), new(12, 0), new(14, 0),
        new(16, 0), new(18, 15), new(20, 0), new(22, 0)
    ];

    private void SetupButton_Click(object sender, RoutedEventArgs e)
    {
        var models = Path.Combine(AppContext.BaseDirectory, "tools", "models");
        var checks = new[]
        {
            ("yt-dlp.exe", ProcessRunner.FindTool("yt-dlp") is not null),
            ("Node.js for YouTube", ProcessRunner.FindTool("node") is not null || ProcessRunner.FindCommand("node") is not null),
            ("ffmpeg.exe", ProcessRunner.FindTool("ffmpeg") is not null),
            ("whisper-cli.exe", ProcessRunner.FindTool("whisper-cli") is not null),
            ("Whisper Large-v3-Turbo", File.Exists(Path.Combine(models, "ggml-large-v3-turbo.bin"))),
            ("Face detection model", File.Exists(Path.Combine(models, "haarcascade_frontalface_default.xml"))),
            ("OpenCV native runtime", File.Exists(Path.Combine(AppContext.BaseDirectory, "OpenCvSharpExtern.dll")) || File.Exists(Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "OpenCvSharpExtern.dll"))),
            ("PNG frame", string.IsNullOrWhiteSpace(_settings.FramePngPath) || File.Exists(_settings.FramePngPath))
        };
        var report = string.Join(Environment.NewLine, checks.Select(item => $"{item.Item1}: {(item.Item2 ? "OK" : "Missing")}"));
        var downloadable = new HashSet<string>(StringComparer.Ordinal)
        {
            "yt-dlp.exe", "Node.js for YouTube", "ffmpeg.exe", "whisper-cli.exe", "Whisper Large-v3-Turbo", "Face detection model"
        };
        var missing = checks.Where(item => !item.Item2 && downloadable.Contains(item.Item1)).Select(item => item.Item1).ToList();
        new SetupWindow(report, missing, _settings) { Owner = this }.ShowDialog();
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
        SetStatus("STATUS: URL ready. Copy the Gemini prompt or import a .py manifest.");
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
            ? "Transcript source set to Whisper (local transcription)."
            : "Transcript source set to automatic YouTube subtitles, with Whisper as a fallback.");
    }

    private void FrameUploadButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "Select a transparent 1080 x 1920 PNG frame",
            Filter = "Transparent PNG (*.png)|*.png",
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
            WriteLog("PNG frame applied to preview and render: " + dialog.FileName);
        }
        catch (Exception ex)
        {
            SetStatus("STATUS: The PNG frame is invalid.", true);
            WpfMessageBox.Show(ex.Message, "PNG frame", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportFontButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "Import a subtitle font",
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
            WriteLog($"Font imported: {importedOption?.FamilyName ?? Path.GetFileName(importedPath)} ({Path.GetFileName(importedPath)})");
        }
        catch (Exception ex)
        {
            SetStatus("STATUS: The font file could not be imported.", true);
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
        FramePathText.Text = "No frame selected";
        WriteLog("PNG frame removed from the preview and render.");
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
        if (decoder.Frames.Count == 0) throw new InvalidDataException("The PNG does not contain a readable image frame.");
        var frame = decoder.Frames[0];
        if (frame.PixelWidth != 1080 || frame.PixelHeight != 1920)
            throw new InvalidDataException($"The frame must be 1080 x 1920 pixels. This file is {frame.PixelWidth} x {frame.PixelHeight}.");
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] < 255) return;
        throw new InvalidDataException("The PNG frame must contain transparent areas so the video remains visible.");
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
            SetStatus("STATUS: Preset names must contain 1 to 32 characters.", true);
            return;
        }

        var existing = _settings.SubtitlePresets.FirstOrDefault(preset =>
            preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null
            && WpfMessageBox.Show($"Replace preset “{existing.Name}” with the current style?", "Save preset",
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
        WriteLog($"Subtitle preset “{name}” saved.");
        SetStatus($"STATUS: Preset “{name}” saved.");
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
        WriteLog($"Subtitle preset “{name}” deleted.");
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
            throw new InvalidOperationException("Custom text colors must use HEX format #RRGGBB.");
        if (SelectedText(HighlightColorBox, "Yellow") == "custom"
            && !System.Text.RegularExpressions.Regex.IsMatch(HighlightColorHexBox.Text.Trim(), @"^#[0-9A-Fa-f]{6}$"))
            throw new InvalidOperationException("Custom highlight colors must use HEX format #RRGGBB.");
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
        var subtitlesDisabled = IsCondensedMode
            ? CondensedSubtitleCheckBox.IsChecked != true
            : ShortClipSubtitleCheckBox.IsChecked != true;
        PreviewCaption.Visibility = subtitlesDisabled
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
        ShortClipSubtitleCheckBox.IsEnabled = !busy;
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
        EditorModeButton.IsEnabled = !busy;
        YouTubeModeButton.IsEnabled = !busy;
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
            WriteLog("Candidate and metadata changes saved to the Gemini manifest.");
        }
        catch (Exception ex)
        {
            SetStatus("STATUS: Changes could not be saved to the manifest.", true);
            WriteLog("ERROR saving manifest: " + ex.Message);
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
            || message.Contains("failed", StringComparison.OrdinalIgnoreCase)
                ? System.Windows.Media.Color.FromRgb(255, 112, 112)
                : message.Contains("warning", StringComparison.OrdinalIgnoreCase)
                    ? System.Windows.Media.Color.FromRgb(255, 204, 102)
                    : message.Contains("Completed", StringComparison.OrdinalIgnoreCase)
                        || message.Contains("successfully", StringComparison.OrdinalIgnoreCase)
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
