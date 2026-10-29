using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SSnip.App.Services;
using SSnip.Core.Downloads;
using SSnip.Core.Formatting;
using SSnip.Core.Media;
using SSnip.Core.Settings;

namespace SSnip.App.ViewModels;

/// <summary>
/// Home: paste a link, see what it is, pick a preset or exact formats, download it.
/// Phase 1 runs one download at a time; the persistent queue arrives in Phase 2.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly IYtDlpService _ytDlp;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger<HomeViewModel> _logger;
    private string? _lastClipboardLink;
    private string? _fetchedLink;
    private bool _outputDirectoryChosen;

    public HomeViewModel(IYtDlpService ytDlp, ISettingsStore settings, IDialogService dialogs, ILogger<HomeViewModel> logger)
    {
        _ytDlp = ytDlp;
        _settings = settings;
        _dialogs = dialogs;
        _logger = logger;
        SelectedPreset = Presets[0];
        OutputDirectory = DefaultOutputDirectory(settings.Current);
        settings.Changed += (_, s) =>
        {
            if (!_outputDirectoryChosen)
            {
                OutputDirectory = DefaultOutputDirectory(s);
            }
        };
    }

    public IReadOnlyList<PresetOption> Presets { get; } =
    [
        new(FormatPreset.EditorSafe, "Editor-safe", "H.264 + AAC in MP4, usually up to 1080p. Imports into DaVinci Resolve as is."),
        new(FormatPreset.MaxQuality, "Max quality", "Best video and audio in MKV, 1440p/4K, often VP9 or AV1. Convert before editing."),
        new(FormatPreset.Cap1080, "1080p cap", "Best quality up to 1080p. Smaller files, enough for most edits."),
        new(FormatPreset.AudioOnly, "Audio only", "Extract the best audio as WAV, MP3, M4A, Opus or FLAC."),
        new(FormatPreset.PreviewProxy, "Preview proxy", "Fast 360p H.264 copy for marking clips."),
    ];

    public IReadOnlyList<AudioFormat> AudioFormats { get; } = Enum.GetValues<AudioFormat>();

    public IReadOnlyList<string> Containers { get; } = ["Automatic", "mp4", "mkv", "mov", "webm"];

    public IReadOnlyList<string> FilenameTemplateChoices { get; } = [.. FilenameTemplates.Presets.Select(p => p.Template)];

    public IReadOnlyList<string> SortChips { get; } = ["res:2160", "res:1440", "res:1080", "res:720", "fps", "vcodec:h264", "vcodec:vp9", "vcodec:av01", "acodec:aac", "+size"];

    public ObservableCollection<FormatRow> Formats { get; } = [];

    public ObservableCollection<FormatChoice> VideoChoices { get; } = [];

    public ObservableCollection<FormatChoice> AudioChoices { get; } = [];

    // ---- Input and info ----

    [ObservableProperty]
    public partial string InputText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInfo), nameof(InfoKindText), nameof(DurationText), nameof(UploadDateText), nameof(ViewsText), nameof(SubtitlesText), nameof(HasChapters), nameof(FilenamePreview), nameof(IsDownloadable))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial MediaInfo? Info { get; private set; }

    [ObservableProperty]
    public partial bool IsFetching { get; private set; }

    [ObservableProperty]
    public partial string? FetchError { get; private set; }

    /// <summary>Why something cannot be done yet, for example a playlist before Phase 2.</summary>
    [ObservableProperty]
    public partial string? Notice { get; private set; }

    public bool HasInfo => Info is not null;

    public bool IsDownloadable => Info?.Kind == MediaKind.Video;

    public bool HasChapters => Info?.Chapters.Count > 0;

    public string InfoKindText => Info?.Kind switch
    {
        MediaKind.Live => "LIVE",
        MediaKind.Upcoming => "UPCOMING",
        MediaKind.Playlist => $"PLAYLIST · {Info.Entries.Count} videos",
        MediaKind.Channel => $"CHANNEL · {Info.Entries.Count} videos",
        _ => Info?.Extractor ?? "",
    };

    public string DurationText => Display.Duration(Info?.Duration);

    public string UploadDateText => Info?.UploadDate?.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? "–";

    public string ViewsText => Info?.ViewCount is null ? "–" : $"{Display.Count(Info.ViewCount)} views";

    public string SubtitlesText
    {
        get
        {
            if (Info is null)
            {
                return "";
            }

            var manual = Info.Subtitles.Where(s => !s.IsAutomatic).Select(s => s.Language).ToList();
            var automatic = Info.Subtitles.Count(s => s.IsAutomatic);
            var manualText = manual.Count == 0 ? "no manual subtitles" : "manual: " + string.Join(", ", manual);
            return automatic == 0 ? manualText : $"{manualText}; {automatic} auto-generated languages";
        }
    }

    // ---- Format selection ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAudioOnlySelected), nameof(FilenamePreview), nameof(EstimatedSizeText))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial bool IsAdvanced { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAudioOnlySelected), nameof(FilenamePreview))]
    public partial PresetOption SelectedPreset { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilenamePreview))]
    public partial AudioFormat SelectedAudioFormat { get; set; } = AudioFormat.Wav;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstimatedSizeText), nameof(FilenamePreview))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial FormatChoice? SelectedVideo { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstimatedSizeText), nameof(FilenamePreview))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial FormatChoice? SelectedAudio { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial string SortString { get; set; } = "";

    /// <summary>Clicking a table row picks it as the video or audio part.</summary>
    [ObservableProperty]
    public partial FormatRow? SelectedFormatRow { get; set; }

    public bool IsAudioOnlySelected => !IsAdvanced && SelectedPreset.Preset == FormatPreset.AudioOnly;

    public string EstimatedSizeText
    {
        get
        {
            var parts = new[] { SelectedVideo?.Format, SelectedAudio?.Format }.Where(f => f is not null).ToList();
            if (parts.Count == 0)
            {
                return "Pick a format to see its size.";
            }

            if (parts.Any(f => f!.FileSize is null))
            {
                return "Estimated size: unknown for some formats";
            }

            var approximate = parts.Any(f => f!.IsFileSizeApproximate);
            return $"Estimated size: {(approximate ? "~" : "")}{Display.Bytes(parts.Sum(f => f!.FileSize!.Value))}";
        }
    }

    // ---- Options ----

    [ObservableProperty]
    public partial string OutputDirectory { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilenamePreview))]
    public partial string FilenameTemplate { get; set; } = FilenameTemplates.Default;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilenamePreview))]
    public partial string SelectedContainer { get; set; } = "Automatic";

    [ObservableProperty]
    public partial bool Overwrite { get; set; }

    [ObservableProperty]
    public partial string RateLimit { get; set; } = "";

    [ObservableProperty]
    public partial int ConcurrentFragments { get; set; } = 4;

    public string FilenamePreview => Info is null || string.IsNullOrWhiteSpace(FilenameTemplate)
        ? ""
        : FilenameTemplatePreview.Render(FilenameTemplate, Info, ExpectedExtension());

    // ---- Job ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsJobVisible), nameof(IsRunning), nameof(IsDone))]
    public partial JobState? JobState { get; private set; }

    [ObservableProperty]
    public partial string StageText { get; private set; } = "";

    [ObservableProperty]
    public partial double ProgressValue { get; private set; }

    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; private set; }

    [ObservableProperty]
    public partial string PercentText { get; private set; } = "";

    [ObservableProperty]
    public partial string SizeText { get; private set; } = "";

    [ObservableProperty]
    public partial string SpeedText { get; private set; } = "";

    [ObservableProperty]
    public partial string EtaText { get; private set; } = "";

    [ObservableProperty]
    public partial string? JobError { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    public partial string? FinalPath { get; private set; }

    [ObservableProperty]
    public partial string? CommandLine { get; private set; }

    [ObservableProperty]
    public partial bool IsCommandVisible { get; set; }

    public bool IsJobVisible => JobState is not null;

    public bool IsRunning => JobState is Core.Downloads.JobState.Downloading or Core.Downloads.JobState.PostProcessing;

    public bool IsDone => JobState == Core.Downloads.JobState.Done;

    // ---- Commands ----

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task FetchAsync(CancellationToken cancellationToken)
    {
        FetchError = null;
        Notice = null;

        var inputs = LinkInputParser.Parse(InputText);
        if (inputs.Count == 0)
        {
            FetchError = "Paste a link first.";
            return;
        }

        var link = inputs.FirstOrDefault(i => i.IsUrl);
        if (link is null)
        {
            Notice = "That looks like a search. Searching inside SSnip arrives in Phase 5; paste a link for now.";
            return;
        }

        if (inputs.Count > 1)
        {
            Notice = $"{inputs.Count} links pasted. Showing the first; queueing several at once arrives with the queue in Phase 2.";
        }

        InputText = link.Text;
        Info = null;
        ClearFormats();
        ResetJob();
        IsFetching = true;
        try
        {
            var info = await _ytDlp.FetchInfoAsync(link.Text, cancellationToken);
            Info = info;
            _fetchedLink = link.Text;
            LoadFormats(info);
            Notice ??= info.Kind switch
            {
                MediaKind.Playlist or MediaKind.Channel => "Downloading playlists and channels arrives in Phase 2. Open a single video from it for now.",
                MediaKind.Live => "Recording live streams arrives in Phase 2.",
                MediaKind.Upcoming => "This stream has not started. Waiting for scheduled streams arrives in Phase 2.",
                _ => null,
            };
        }
        catch (YtDlpException ex)
        {
            FetchError = ex.Message;
        }
        catch (FileNotFoundException)
        {
            FetchError = "yt-dlp is not installed. Open Settings › Tools to download it.";
        }
        catch (OperationCanceledException)
        {
            FetchError = "Fetch cancelled.";
        }
        finally
        {
            IsFetching = false;
        }
    }

    /// <summary>Ctrl+V outside the link box: paste the clipboard and fetch it.</summary>
    [RelayCommand]
    private async Task PasteAndFetchAsync()
    {
        var text = _dialogs.GetClipboardText();
        if (!string.IsNullOrWhiteSpace(text))
        {
            InputText = text.Trim();
            await FetchCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Ctrl+Enter: download with the default preset, fetching first if needed.</summary>
    [RelayCommand]
    private async Task DownloadWithDefaultPresetAsync()
    {
        if (Info is null || LinkInputParser.Classify(InputText).Text != _fetchedLink)
        {
            await FetchCommand.ExecuteAsync(null);
        }

        if (Info is null)
        {
            return;
        }

        IsAdvanced = false;
        SelectedPreset = Presets[0];
        if (DownloadCommand.CanExecute(null))
        {
            await DownloadCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync(CancellationToken cancellationToken)
    {
        DownloadOptions options;
        try
        {
            options = BuildOptions();
            CommandLine = _ytDlp.DescribeCommand(options);
        }
        catch (ArgumentException ex)
        {
            JobState = Core.Downloads.JobState.Failed;
            JobError = ex.Message;
            return;
        }

        var info = Info;
        ResetJob();
        JobState = Core.Downloads.JobState.Downloading;
        StageText = DownloadStageResolver.Describe(DownloadStage.Starting);
        IsProgressIndeterminate = true;

        // Created on the UI thread, so events from yt-dlp's reader threads are marshalled here in order.
        var progress = new Progress<YtDlpEvent>(e => OnEvent(e, info));
        try
        {
            var result = await _ytDlp.DownloadAsync(options, progress, cancellationToken);
            CommandLine = result.CommandLine;
            if (result.Succeeded)
            {
                JobState = Core.Downloads.JobState.Done;
                FinalPath = result.FilePath;
                StageText = result.WasSkipped
                    ? "Already in the folder, so nothing was downloaded. Turn on Overwrite in Options to download it again."
                    : $"Done in {Display.Duration(result.Duration)}";
                IsProgressIndeterminate = false;
                ProgressValue = 100;
                PercentText = "100%";
                SpeedText = "";
                EtaText = "";
                if (File.Exists(result.FilePath))
                {
                    SizeText = Display.Bytes(new FileInfo(result.FilePath!).Length);
                }
            }
            else
            {
                Fail(result.Errors.LastOrDefault() ?? $"yt-dlp exited with code {result.ExitCode}.");
            }
        }
        catch (OperationCanceledException)
        {
            JobState = Core.Downloads.JobState.Cancelled;
            StageText = "Cancelled. Partial files are kept, so downloading again resumes.";
            IsProgressIndeterminate = false;
            SpeedText = "";
            EtaText = "";
        }
        catch (FileNotFoundException)
        {
            Fail("yt-dlp is not installed. Open Settings › Tools to download it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Download failed");
            Fail(ex.Message);
        }
    }

    private bool CanDownload() =>
        IsDownloadable && (!IsAdvanced || SelectedVideo?.Format is not null || SelectedAudio?.Format is not null || !string.IsNullOrWhiteSpace(SortString));

    [RelayCommand]
    private void ToggleCommand()
    {
        if (!IsCommandVisible)
        {
            try
            {
                // Before a download starts, show what it will run.
                if (!IsRunning && Info is not null)
                {
                    CommandLine = _ytDlp.DescribeCommand(BuildOptions());
                }
            }
            catch (ArgumentException ex)
            {
                CommandLine = $"Cannot build the command: {ex.Message}";
            }
        }

        IsCommandVisible = !IsCommandVisible;
    }

    [RelayCommand]
    private void CopyCommand()
    {
        if (CommandLine is not null)
        {
            _dialogs.SetClipboardText(CommandLine);
        }
    }

    [RelayCommand(CanExecute = nameof(HasFinalPath))]
    private void OpenFolder() => _dialogs.RevealInExplorer(FinalPath!);

    private bool HasFinalPath() => FinalPath is not null;

    [RelayCommand]
    private void ChangeOutputDirectory()
    {
        var folder = _dialogs.PickFolder("Choose where downloads go", OutputDirectory);
        if (folder is not null)
        {
            OutputDirectory = folder;
            _outputDirectoryChosen = true;
        }
    }

    [RelayCommand]
    private void ToggleSortChip(string chip)
    {
        var parts = SortString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (!parts.Remove(chip))
        {
            // A new res: chip replaces any other res: chip, and so on for each field.
            var field = chip.TrimStart('+').Split(':')[0];
            parts.RemoveAll(p => p.TrimStart('+').Split(':')[0] == field);
            parts.Add(chip);
        }

        SortString = string.Join(",", parts);
    }

    /// <summary>Called when the app window gets focus, for the "fetch the clipboard" setting.</summary>
    public async Task OnAppActivatedAsync()
    {
        if (!_settings.Current.FetchClipboardOnFocus || IsFetching || IsRunning)
        {
            return;
        }

        var text = _dialogs.GetClipboardText();
        var link = text is null ? null : LinkInputParser.Parse(text).FirstOrDefault(i => i.IsUrl);
        if (link is null || link.Text == _lastClipboardLink || link.Text == InputText)
        {
            return;
        }

        _lastClipboardLink = link.Text;
        InputText = link.Text;
        await FetchCommand.ExecuteAsync(null);
    }

    /// <summary>Links dropped onto the page.</summary>
    public async Task OnLinksDroppedAsync(string text)
    {
        InputText = text.Trim();
        await FetchCommand.ExecuteAsync(null);
    }

    partial void OnSelectedFormatRowChanged(FormatRow? value)
    {
        if (value is null)
        {
            return;
        }

        if (value.Format.HasVideo)
        {
            SelectedVideo = VideoChoices.FirstOrDefault(c => c.Format == value.Format);

            // A format that already has audio needs no separate audio part.
            if (value.Format.HasAudio)
            {
                SelectedAudio = AudioChoices[0];
            }
        }
        else if (value.Format.HasAudio)
        {
            SelectedAudio = AudioChoices.FirstOrDefault(c => c.Format == value.Format);
        }
    }

    private void OnEvent(YtDlpEvent e, MediaInfo? info)
    {
        switch (e)
        {
            case DownloadProgressEvent p:
                JobState = Core.Downloads.JobState.Downloading;
                StageText = DownloadStageResolver.Describe(DownloadStageResolver.FromFileName(p.FileName, info));
                if (p.Fraction is { } fraction)
                {
                    IsProgressIndeterminate = false;
                    ProgressValue = fraction * 100;
                    PercentText = Display.Percent(fraction);
                    SizeText = $"{Display.Bytes(p.DownloadedBytes)} of {(p.IsTotalEstimate ? "~" : "")}{Display.Bytes(p.TotalBytes)}";
                }
                else
                {
                    IsProgressIndeterminate = true;
                    PercentText = p.FragmentCount is { } count ? $"fragment {p.FragmentIndex}/{count}" : "";
                    SizeText = Display.Bytes(p.DownloadedBytes);
                }

                SpeedText = p.IsFinished ? "" : Display.Speed(p.BytesPerSecond);
                EtaText = p.IsFinished || p.Eta is null ? "" : $"ETA {Display.Duration(p.Eta)}";
                break;

            case PostProcessEvent { IsStarted: true } pp:
                JobState = Core.Downloads.JobState.PostProcessing;
                StageText = DownloadStageResolver.Describe(DownloadStageResolver.FromPostProcessor(pp.PostProcessor));
                IsProgressIndeterminate = true;
                SpeedText = "";
                EtaText = "";
                break;
        }
    }

    private DownloadOptions BuildOptions()
    {
        var url = Info?.WebpageUrl ?? InputText;
        return new DownloadOptions
        {
            Url = url,
            OutputDirectory = OutputDirectory,
            Preset = IsAdvanced ? FormatPreset.Custom : SelectedPreset.Preset,
            Custom = IsAdvanced ? new FormatSelection(SelectedVideo?.Format?.FormatId, SelectedAudio?.Format?.FormatId, SortString) : null,
            FilenameTemplate = FilenameTemplate,
            MergeContainer = SelectedContainer == "Automatic" ? null : SelectedContainer,
            AudioFormat = SelectedAudioFormat,
            Overwrite = Overwrite,
            RateLimit = string.IsNullOrWhiteSpace(RateLimit) ? null : RateLimit,
            ConcurrentFragments = ConcurrentFragments,
        };
    }

    private string ExpectedExtension()
    {
        if (SelectedContainer != "Automatic")
        {
            return SelectedContainer;
        }

        if (IsAdvanced)
        {
            var video = SelectedVideo?.Format;
            var audio = SelectedAudio?.Format;
            return video is not null && audio is not null ? (video.Ext == audio.Ext || (video.Ext == "mp4" && audio.Ext == "m4a") ? "mp4" : "mkv") : video?.Ext ?? audio?.Ext ?? "mp4";
        }

        return SelectedPreset.Preset switch
        {
            FormatPreset.MaxQuality => "mkv",
            FormatPreset.AudioOnly => SelectedAudioFormat.ToString().ToLowerInvariant(),
            FormatPreset.Cap1080 => "webm",
            _ => "mp4",
        };
    }

    private void LoadFormats(MediaInfo info)
    {
        foreach (var format in info.Formats.Where(f => !f.IsStoryboard).OrderByDescending(f => f.HasVideo).ThenByDescending(f => f.Height).ThenByDescending(f => f.Bitrate))
        {
            Formats.Add(new FormatRow(format));
        }

        VideoChoices.Add(FormatChoice.None("None (audio only)"));
        foreach (var format in info.Formats.Where(f => f.HasVideo).OrderByDescending(f => f.Height).ThenByDescending(f => f.Bitrate))
        {
            VideoChoices.Add(FormatChoice.For(format));
        }

        AudioChoices.Add(FormatChoice.None("None"));
        foreach (var format in info.Formats.Where(f => f.IsAudioOnly).OrderByDescending(f => f.Bitrate))
        {
            AudioChoices.Add(FormatChoice.For(format));
        }

        // Start Advanced mode on the best video-only and audio-only pair.
        SelectedVideo = VideoChoices.Skip(1).FirstOrDefault(c => c.Format!.IsVideoOnly) ?? VideoChoices.Skip(1).FirstOrDefault() ?? VideoChoices[0];
        SelectedAudio = SelectedVideo.Format is { HasAudio: true } ? AudioChoices[0] : AudioChoices.Skip(1).FirstOrDefault() ?? AudioChoices[0];
        OnPropertyChanged(nameof(EstimatedSizeText));
    }

    private void ClearFormats()
    {
        Formats.Clear();
        VideoChoices.Clear();
        AudioChoices.Clear();
        SelectedVideo = null;
        SelectedAudio = null;
        SelectedFormatRow = null;
    }

    private void ResetJob()
    {
        JobState = null;
        JobError = null;
        FinalPath = null;
        StageText = "";
        ProgressValue = 0;
        PercentText = "";
        SizeText = "";
        SpeedText = "";
        EtaText = "";
    }

    private void Fail(string message)
    {
        JobState = Core.Downloads.JobState.Failed;
        JobError = message;
        StageText = "Failed";
        IsProgressIndeterminate = false;
        SpeedText = "";
        EtaText = "";
    }

    private static string DefaultOutputDirectory(AppSettings settings) =>
        Path.Combine(
            settings.LibraryRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "SSnip"),
            "Inbox",
            "raw");
}
