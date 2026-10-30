using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SSnip.App.Services;
using SSnip.Core;
using SSnip.Core.Downloads;
using SSnip.Core.Formatting;
using SSnip.Core.Media;
using SSnip.Core.Queue;
using SSnip.Core.Settings;

namespace SSnip.App.ViewModels;

/// <summary>
/// Home: paste a link, see what it is, pick a preset or exact formats and extras, and send it to
/// the queue. A single video's progress shows here too; playlists and channels list their videos
/// to pick from.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly IYtDlpService _ytDlp;
    private readonly JobQueue _queue;
    private readonly QueueViewModel _queueView;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly AppPaths _paths;
    private readonly ILogger<HomeViewModel> _logger;
    private string? _lastClipboardLink;
    private string? _fetchedLink;
    private bool _outputDirectoryChosen;
    private bool _switchingTab;

    public HomeViewModel(IYtDlpService ytDlp, JobQueue queue, QueueViewModel queueView, ISettingsStore settings, IDialogService dialogs, AppPaths paths, ILogger<HomeViewModel> logger)
    {
        _ytDlp = ytDlp;
        _queue = queue;
        _queueView = queueView;
        _settings = settings;
        _dialogs = dialogs;
        _paths = paths;
        _logger = logger;
        SelectedPreset = Presets[0];
        SelectedSubtitleMode = SubtitleModes[0];
        SelectedChannelTab = ChannelTabChoices[0];
        OutputDirectory = DefaultOutputDirectory(settings.Current);
        SubtitleLanguagesText = settings.Current.SubtitleLanguages;
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

    public IReadOnlyList<SubtitleModeOption> SubtitleModes { get; } =
    [
        new(SubtitleMode.Srt, "Separate .srt files"),
        new(SubtitleMode.Embed, "Embedded in the video"),
        new(SubtitleMode.SrtAndEmbed, "Both"),
    ];

    public IReadOnlyList<ChannelTabOption> ChannelTabChoices { get; } =
    [
        new(ChannelTab.Videos, "Videos"),
        new(ChannelTab.Shorts, "Shorts"),
        new(ChannelTab.Live, "Live"),
    ];

    public IReadOnlyList<SponsorBlockCategoryViewModel> SponsorBlockCategories { get; } = SponsorBlockCategoryViewModel.FootageDefaults();

    public ObservableCollection<FormatRow> Formats { get; } = [];

    public ObservableCollection<FormatChoice> VideoChoices { get; } = [];

    public ObservableCollection<FormatChoice> AudioChoices { get; } = [];

    /// <summary>Every entry of a playlist or channel.</summary>
    public List<PlaylistEntryViewModel> AllEntries { get; } = [];

    /// <summary>The entries that pass the filters.</summary>
    public ObservableCollection<PlaylistEntryViewModel> Entries { get; } = [];

    // ---- Input and info ----

    [ObservableProperty]
    public partial string InputText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInfo), nameof(InfoKindText), nameof(DurationText), nameof(UploadDateText), nameof(ViewsText), nameof(SubtitlesText), nameof(HasChapters), nameof(FilenamePreview), nameof(IsDownloadable), nameof(IsPlaylist), nameof(IsChannelWithTabs), nameof(IsLive), nameof(IsUpcoming), nameof(DownloadText))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial MediaInfo? Info { get; private set; }

    [ObservableProperty]
    public partial bool IsFetching { get; private set; }

    [ObservableProperty]
    public partial string? FetchError { get; private set; }

    [ObservableProperty]
    public partial string? Notice { get; private set; }

    /// <summary>Links pasted together, beyond the one shown; "Add all to queue" sends them with the current settings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingLinks), nameof(AddLinksText))]
    public partial IReadOnlyList<string> PendingLinks { get; private set; } = [];

    public bool HasPendingLinks => PendingLinks.Count > 1;

    public string AddLinksText => $"Add all {PendingLinks.Count} links to the queue";

    public bool HasInfo => Info is not null;

    public bool IsDownloadable => Info?.IsSingleVideo == true;

    public bool IsPlaylist => Info?.Kind is MediaKind.Playlist or MediaKind.Channel;

    public bool IsChannelWithTabs => Info?.Kind == MediaKind.Channel && _fetchedLink is not null && ChannelTabs.IsChannel(_fetchedLink);

    public bool IsLive => Info?.Kind is MediaKind.Live or MediaKind.Upcoming;

    public bool IsUpcoming => Info?.Kind == MediaKind.Upcoming;

    public bool HasChapters => Info?.Chapters.Count > 0;

    public string DownloadText => Info?.Kind switch
    {
        MediaKind.Live => "Record",
        MediaKind.Upcoming => "Record when it starts",
        _ => "Download",
    };

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

    // ---- Extras: subtitles, metadata, SponsorBlock ----

    [ObservableProperty]
    public partial bool SubtitlesEnabled { get; set; }

    [ObservableProperty]
    public partial string SubtitleLanguagesText { get; set; }

    [ObservableProperty]
    public partial bool IncludeAutoSubtitles { get; set; } = true;

    [ObservableProperty]
    public partial SubtitleModeOption SelectedSubtitleMode { get; set; }

    [ObservableProperty]
    public partial bool EmbedMetadata { get; set; } = true;

    [ObservableProperty]
    public partial bool EmbedChapters { get; set; } = true;

    /// <summary>Off by default: some editors read an embedded cover as a second video stream.</summary>
    [ObservableProperty]
    public partial bool EmbedThumbnail { get; set; }

    [ObservableProperty]
    public partial bool SaveComments { get; set; }

    [ObservableProperty]
    public partial bool SponsorBlockEnabled { get; set; } = true;

    // ---- Live ----

    [ObservableProperty]
    public partial bool LiveFromStart { get; set; }

    // ---- Playlist and channel ----

    [ObservableProperty]
    public partial ChannelTabOption SelectedChannelTab { get; set; }

    [ObservableProperty]
    public partial string TitleFilter { get; set; } = "";

    [ObservableProperty]
    public partial double? MinMinutes { get; set; }

    [ObservableProperty]
    public partial double? MaxMinutes { get; set; }

    [ObservableProperty]
    public partial string ItemList { get; set; } = "";

    [ObservableProperty]
    public partial DateTime? DateAfter { get; set; }

    [ObservableProperty]
    public partial DateTime? DateBefore { get; set; }

    [ObservableProperty]
    public partial double? MaxDownloads { get; set; }

    [ObservableProperty]
    public partial string? PlaylistError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddSelectedText))]
    [NotifyCanExecuteChangedFor(nameof(AddSelectedToQueueCommand))]
    public partial int SelectedCount { get; private set; }

    public string AddSelectedText
    {
        get
        {
            var count = MaxDownloads is > 0 ? Math.Min(SelectedCount, (int)MaxDownloads.Value) : SelectedCount;
            return count == 1 ? "Add 1 video to the queue" : $"Add {count} videos to the queue";
        }
    }

    public string EntriesSummary => $"{SelectedCount} of {Entries.Count} shown selected · {AllEntries.Count} in total";

    // ---- The job started from Home ----

    /// <summary>The last single video sent to the queue from Home; its progress shows here.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsJobVisible))]
    public partial JobViewModel? TrackedJob { get; private set; }

    [ObservableProperty]
    public partial string? DownloadError { get; private set; }

    [ObservableProperty]
    public partial string? CommandLine { get; private set; }

    [ObservableProperty]
    public partial bool IsCommandVisible { get; set; }

    public bool IsJobVisible => TrackedJob is not null;

    /// <summary>Asks the shell to show the Queue screen.</summary>
    public event EventHandler? OpenQueueRequested;

    // ---- Commands ----

    [RelayCommand]
    private void OpenQueue() => OpenQueueRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task FetchAsync(CancellationToken cancellationToken)
    {
        FetchError = null;
        Notice = null;
        PlaylistError = null;

        var inputs = LinkInputParser.Parse(InputText);
        if (inputs.Count == 0)
        {
            FetchError = "Paste a link first.";
            return;
        }

        var links = inputs.Where(i => i.IsUrl).Select(i => i.Text).ToList();
        if (links.Count == 0)
        {
            Notice = "That looks like a search. Searching inside SSnip arrives in Phase 5; paste a link for now.";
            return;
        }

        PendingLinks = links;
        if (links.Count > 1)
        {
            Notice = $"{links.Count} links pasted. The first is shown below; add them all to the queue with the current settings, or fetch one at a time.";
        }

        // A bare YouTube channel lists its tabs, not its videos, so open the chosen tab.
        var link = links[0];
        if (ChannelTabs.IsChannel(link) && ChannelTabs.TabOf(link) is null)
        {
            link = ChannelTabs.UrlFor(link, SelectedChannelTab.Tab)!;
        }
        else if (ChannelTabs.TabOf(link) is { } tab && tab != SelectedChannelTab.Tab)
        {
            _switchingTab = true;
            SelectedChannelTab = ChannelTabChoices.First(c => c.Tab == tab);
            _switchingTab = false;
        }

        if (links.Count == 1)
        {
            InputText = link;
        }

        Info = null;
        ClearFormats();
        ClearEntries();
        IsFetching = true;
        try
        {
            var info = await _ytDlp.FetchInfoAsync(link, cancellationToken);
            _fetchedLink = link;
            Info = info;
            LoadFormats(info);
            LoadEntries(info);

            // Exact formats belong to one video; a playlist uses a preset for every video.
            if (IsPlaylist)
            {
                IsAdvanced = false;
            }
            if (info.Kind == MediaKind.Upcoming)
            {
                Notice ??= "This stream has not started. SSnip checks every minute and records once it goes live.";
            }
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

    /// <summary>Sends the shown video (or live stream) to the queue and follows its progress here.</summary>
    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        DownloadError = null;
        if (!TryBuildOptions(Info?.WebpageUrl ?? InputText, out var options))
        {
            return;
        }

        CommandLine = _ytDlp.DescribeCommand(options);
        var added = await _queue.AddAsync([new NewJob(options, Info?.Title)]);
        TrackedJob = _queueView.Upsert(added[0]);
    }

    private bool CanDownload() =>
        IsDownloadable && (!IsAdvanced || SelectedVideo?.Format is not null || SelectedAudio?.Format is not null || !string.IsNullOrWhiteSpace(SortString));

    [RelayCommand]
    private async Task AddLinksToQueueAsync()
    {
        DownloadError = null;
        var jobs = new List<NewJob>();
        foreach (var link in PendingLinks)
        {
            if (!TryBuildOptions(link, out var options, useAdvanced: false))
            {
                return;
            }

            jobs.Add(new NewJob(options));
        }

        await _queue.AddAsync(jobs);
        Notice = $"Added {jobs.Count} links to the queue. Titles appear in the Queue as each one starts.";
        PendingLinks = [];
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task AddSelectedToQueueAsync()
    {
        DownloadError = null;
        var chosen = Entries.Where(e => e.IsSelected).ToList();
        if (MaxDownloads is > 0)
        {
            chosen = [.. chosen.Take((int)MaxDownloads.Value)];
        }

        var jobs = new List<NewJob>(chosen.Count);
        foreach (var entry in chosen)
        {
            if (!TryBuildOptions(entry.Url, out var options, useAdvanced: false))
            {
                return;
            }

            jobs.Add(new NewJob(options, entry.Title));
        }

        await _queue.AddAsync(jobs);
        Notice = $"Added {jobs.Count} videos to the queue.";
    }

    private bool HasSelection() => SelectedCount > 0;

    [RelayCommand]
    private void SelectAll() => SetSelected(_ => true);

    [RelayCommand]
    private void SelectNone() => SetSelected(_ => false);

    /// <summary>Selects exactly the items in the item list, such as <c>1:10,15</c>.</summary>
    [RelayCommand]
    private void ApplyItemList()
    {
        PlaylistError = null;
        try
        {
            var items = PlaylistItems.Parse(ItemList, AllEntries.Count).ToHashSet();
            foreach (var entry in AllEntries)
            {
                entry.IsSelected = items.Contains(entry.Index);
            }
        }
        catch (FormatException ex)
        {
            PlaylistError = ex.Message;
        }
    }

    [RelayCommand]
    private void CancelTracked()
    {
        if (TrackedJob is not null)
        {
            _queue.Cancel(TrackedJob.Id);
        }
    }

    [RelayCommand]
    private void StopTrackedRecording()
    {
        if (TrackedJob is not null)
        {
            _queue.StopRecording(TrackedJob.Id);
        }
    }

    [RelayCommand]
    private void ToggleCommand()
    {
        if (!IsCommandVisible)
        {
            if (TrackedJob?.Job.CommandLine is { } running && TrackedJob.IsActive)
            {
                CommandLine = running;
            }
            else if (Info is not null)
            {
                // Before a download starts, show what it will run.
                CommandLine = TryBuildOptions(Info.WebpageUrl ?? InputText, out var options)
                    ? _ytDlp.DescribeCommand(options)
                    : $"Cannot build the command: {DownloadError}";
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

    [RelayCommand]
    private void OpenFolder()
    {
        if (TrackedJob?.Job.FilePath is { } path)
        {
            _dialogs.RevealInExplorer(path);
        }
        else
        {
            _dialogs.OpenInExplorer(OutputDirectory);
        }
    }

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
        if (!_settings.Current.FetchClipboardOnFocus || IsFetching)
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

    partial void OnSelectedChannelTabChanged(ChannelTabOption value)
    {
        if (_switchingTab || !IsChannelWithTabs || _fetchedLink is null || ChannelTabs.UrlFor(_fetchedLink, value.Tab) is not { } url)
        {
            return;
        }

        InputText = url;
        FetchCommand.Execute(null);
    }

    partial void OnTitleFilterChanged(string value) => ApplyFilter();

    partial void OnMinMinutesChanged(double? value) => ApplyFilter();

    partial void OnMaxMinutesChanged(double? value) => ApplyFilter();

    partial void OnMaxDownloadsChanged(double? value) => OnPropertyChanged(nameof(AddSelectedText));

    private void ApplyFilter()
    {
        var filter = new PlaylistFilter(
            TitleFilter,
            MinMinutes is > 0 ? TimeSpan.FromMinutes(MinMinutes.Value) : null,
            MaxMinutes is > 0 ? TimeSpan.FromMinutes(MaxMinutes.Value) : null);

        PlaylistError = filter.Validate();
        Entries.Clear();
        foreach (var entry in AllEntries.Where(e => PlaylistError is not null || filter.Matches(e.Entry)))
        {
            Entries.Add(entry);
        }

        UpdateSelectedCount();
    }

    private void SetSelected(Func<PlaylistEntryViewModel, bool> selected)
    {
        foreach (var entry in Entries)
        {
            entry.IsSelected = selected(entry);
        }
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = Entries.Count(e => e.IsSelected);
        OnPropertyChanged(nameof(EntriesSummary));
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaylistEntryViewModel.IsSelected))
        {
            UpdateSelectedCount();
        }
    }

    private void LoadEntries(MediaInfo info)
    {
        var index = 1;
        foreach (var entry in info.Entries)
        {
            var row = new PlaylistEntryViewModel(index++, entry);
            row.PropertyChanged += OnEntryChanged;
            AllEntries.Add(row);
        }

        ApplyFilter();
    }

    private void ClearEntries()
    {
        foreach (var entry in AllEntries)
        {
            entry.PropertyChanged -= OnEntryChanged;
        }

        AllEntries.Clear();
        Entries.Clear();
        UpdateSelectedCount();
    }

    /// <summary>Builds the options for one link from everything on the page; reports a problem in <see cref="DownloadError"/>.</summary>
    private bool TryBuildOptions(string url, out DownloadOptions options, bool useAdvanced = true)
    {
        options = null!;
        var advanced = useAdvanced && IsAdvanced && IsDownloadable;
        var preset = advanced ? FormatPreset.Custom : SelectedPreset.Preset;
        try
        {
            var languages = SubtitleLanguagesText.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var sponsorBlock = SponsorBlockEnabled
                ? new SponsorBlockOptions(
                    [.. SponsorBlockCategories.Where(c => c.Action == SponsorBlockAction.Remove).Select(c => c.Category)],
                    [.. SponsorBlockCategories.Where(c => c.Action == SponsorBlockAction.Mark).Select(c => c.Category)])
                : null;

            options = new DownloadOptions
            {
                Url = url,
                OutputDirectory = OutputDirectory,
                Preset = preset,
                Custom = advanced ? new FormatSelection(SelectedVideo?.Format?.FormatId, SelectedAudio?.Format?.FormatId, SortString) : null,
                FilenameTemplate = FilenameTemplate,
                MergeContainer = SelectedContainer == "Automatic" || preset == FormatPreset.AudioOnly ? null : SelectedContainer,
                AudioFormat = SelectedAudioFormat,
                Overwrite = Overwrite,
                RateLimit = string.IsNullOrWhiteSpace(RateLimit) ? null : RateLimit,
                ConcurrentFragments = ConcurrentFragments,
                Subtitles = SubtitlesEnabled && SubtitlePicker.Pick(languages, IsShownVideo(url) ? Info!.Subtitles : [], IncludeAutoSubtitles) is { } language
                    ? new SubtitleOptions([language], IncludeAutoSubtitles, preset == FormatPreset.AudioOnly ? SubtitleMode.Srt : SelectedSubtitleMode.Mode)
                    : null,
                EmbedMetadata = EmbedMetadata,
                EmbedChapters = EmbedChapters,
                EmbedThumbnail = EmbedThumbnail,
                InfoJsonDirectory = _paths.InfoJson,
                MaxComments = SaveComments ? 200 : null,
                SponsorBlock = sponsorBlock,
                DateAfter = IsPlaylist && DateAfter is { } after ? DateOnly.FromDateTime(after) : null,
                DateBefore = IsPlaylist && DateBefore is { } before ? DateOnly.FromDateTime(before) : null,
                Live = Info?.Kind switch
                {
                    MediaKind.Live when url == (Info.WebpageUrl ?? InputText) => new LiveOptions(LiveFromStart),
                    MediaKind.Upcoming when url == (Info.WebpageUrl ?? InputText) => new LiveOptions(LiveFromStart, WaitForVideoSeconds: 60),
                    _ => null,
                },
            };

            // Validates every option the same way the job will.
            _ = YtDlpArgsBuilder.Build(options);
            return true;
        }
        catch (ArgumentException ex)
        {
            DownloadError = ex.Message;
            return false;
        }
    }

    /// <summary>True when <paramref name="url"/> is the single video whose info is shown, so its subtitle tracks are known.</summary>
    private bool IsShownVideo(string url) => IsDownloadable && url == (Info!.WebpageUrl ?? InputText);

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

    private static string DefaultOutputDirectory(AppSettings settings) =>
        Path.Combine(
            settings.LibraryRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "SSnip"),
            "Inbox",
            "raw");
}
