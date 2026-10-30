using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;
using SSnip.App.Services;
using SSnip.Core;
using SSnip.Core.Clipping;
using SSnip.Core.Downloads;
using SSnip.Core.Media;
using SSnip.Core.Queue;
using SSnip.Core.Settings;
using SSnip.Infrastructure.Clipping;

namespace SSnip.App.ViewModels;

/// <summary>
/// The clip editor: plays a preview proxy (or a local file), marks in and out points, and sends the
/// clips to the queue as full-quality section downloads or local cuts.
/// </summary>
public sealed partial class ClipEditorViewModel : ObservableObject, IDisposable
{
    private static readonly float[] Speeds = [1, 2, 4, 8];

    private readonly JobQueue _queue;
    private readonly QueueViewModel _queueView;
    private readonly IClipStore _clipStore;
    private readonly IFfmpegService _ffmpeg;
    private readonly IThumbnailDownloader _thumbnails;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly AppPaths _paths;
    private readonly ILogger<ClipEditorViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly LibVLC _libVlc;
    private readonly Stack<IReadOnlyList<ClipMark>> _undo = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly DispatcherTimer _reverse;
    private int _speedIndex;
    private int _reverseIndex = -1;
    private bool _positionFromPlayer;
    private long? _seekTarget;
    private readonly TaskCompletionSource _viewAttached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _restoring;
    private EncoderSupport? _encoders;

    public ClipEditorViewModel(JobQueue queue, QueueViewModel queueView, IClipStore clipStore, IFfmpegService ffmpeg, IThumbnailDownloader thumbnails, ISettingsStore settings, IDialogService dialogs, AppPaths paths, ILogger<ClipEditorViewModel> logger)
    {
        _queue = queue;
        _queueView = queueView;
        _clipStore = clipStore;
        _ffmpeg = ffmpeg;
        _thumbnails = thumbnails;
        _settings = settings;
        _dialogs = dialogs;
        _paths = paths;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;

        LibVLCSharp.Shared.Core.Initialize();
        // Subtitles next to a video (an .srt SSnip saved) would otherwise be loaded and cover the picture.
        _libVlc = new LibVLC("--no-video-title-show", "--quiet", "--no-sub-autodetect-file");
        Player = new MediaPlayer(_libVlc) { EnableHardwareDecoding = true };
        Player.TimeChanged += (_, e) => _dispatcher.BeginInvoke(() => SetPositionFromPlayer(e.Time));
        Player.LengthChanged += (_, e) => _dispatcher.BeginInvoke(() => LengthMs = e.Length);
        Player.Playing += (_, _) => _dispatcher.BeginInvoke(() => IsPlaying = true);
        Player.Paused += (_, _) => _dispatcher.BeginInvoke(() => IsPlaying = false);
        Player.Stopped += (_, _) => _dispatcher.BeginInvoke(() => IsPlaying = false);
        Player.EndReached += (_, _) => _dispatcher.BeginInvoke(() => IsPlaying = false);

        // LibVLC cannot play backwards, so J steps back a little many times a second.
        _reverse = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        _reverse.Tick += (_, _) => Seek(PositionMs - (long)(100 * Speeds[Math.Max(0, _reverseIndex)]));

        var clipping = settings.Current.Clipping;
        HandleSeconds = clipping.HandleSeconds;
        SelectedCutMode = CutModes.First(m => m.Mode == clipping.CutMode);
        SelectedProfile = Profiles.First(p => p.Kind == clipping.Profile);
        TargetFps = clipping.TargetFps;
        TargetHeight = clipping.TargetHeight;
        ConstantFrameRate = clipping.ConstantFrameRate;
        PcmAudio = clipping.Audio == ProfileAudio.Pcm48k;
        SelectedQuality = Qualities[0];
        Clips.CollectionChanged += (_, _) => OnClipsChanged();
    }

    public MediaPlayer Player { get; }

    public ObservableCollection<ClipRowViewModel> Clips { get; } = [];

    public IReadOnlyList<CutModeOption> CutModes { get; } =
    [
        new(CutMode.Accurate, "Accurate (re-encodes at the exact marks)"),
        new(CutMode.Fast, "Fast (cuts on keyframes, no re-encode)"),
    ];

    public IReadOnlyList<ProfileOption> Profiles { get; } =
    [
        new(EditorProfileKind.H264, "H.264 MP4"),
        new(EditorProfileKind.DnxhrHq, "DNxHR HQ (MOV)"),
        new(EditorProfileKind.ProRes422, "ProRes 422 (MOV)"),
        new(EditorProfileKind.KeepOriginal, "Keep original"),
    ];

    public IReadOnlyList<PresetOption> Qualities { get; } =
    [
        new(FormatPreset.EditorSafe, "Editor-safe", "H.264 + AAC, usually up to 1080p"),
        new(FormatPreset.MaxQuality, "Max quality", "1440p/4K, often VP9 or AV1"),
        new(FormatPreset.Cap1080, "1080p cap", "Best up to 1080p"),
    ];

    public IReadOnlyList<int?> FpsChoices { get; } = [null, 30, 60];

    public IReadOnlyList<int?> HeightChoices { get; } = [null, 1080, 1440, 2160];

    // ---- Source ----

    /// <summary>The video URL or local path the clips belong to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSource))]
    public partial string? SourceKey { get; private set; }

    [ObservableProperty]
    public partial string SourceTitle { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanScanDescription), nameof(HasChapters))]
    public partial MediaInfo? Info { get; private set; }

    [ObservableProperty]
    public partial bool IsLocal { get; private set; }

    /// <summary>The file playing: the proxy for a URL, the file itself for a local source.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedia))]
    [NotifyCanExecuteChangedFor(nameof(GrabFramesEveryCommand), nameof(ContactSheetCommand))]
    public partial string? MediaPath { get; private set; }

    /// <summary>The proxy download, while it runs.</summary>
    [ObservableProperty]
    public partial JobViewModel? ProxyJob { get; private set; }

    [ObservableProperty]
    public partial string? Status { get; private set; }

    public bool HasSource => SourceKey is not null;

    public bool HasMedia => MediaPath is not null;

    public bool CanScanDescription => !string.IsNullOrWhiteSpace(Info?.Description);

    public bool HasChapters => Info?.Chapters.Count > 0;

    // ---- Playback ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    public partial long PositionMs { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LengthText))]
    public partial long LengthMs { get; private set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; private set; }

    [ObservableProperty]
    public partial string SpeedText { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InText), nameof(MarkLengthText))]
    [NotifyCanExecuteChangedFor(nameof(AddClipCommand))]
    public partial TimeSpan? InPoint { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutText), nameof(MarkLengthText))]
    [NotifyCanExecuteChangedFor(nameof(AddClipCommand))]
    public partial TimeSpan? OutPoint { get; private set; }

    [ObservableProperty]
    public partial ClipRowViewModel? SelectedClip { get; set; }

    public string PositionText => ClipTime.Display(TimeSpan.FromMilliseconds(PositionMs));

    public string LengthText => ClipTime.Display(TimeSpan.FromMilliseconds(LengthMs));

    public string InText => InPoint is { } i ? ClipTime.Display(i) : "–";

    public string OutText => OutPoint is { } o ? ClipTime.Display(o) : "–";

    public string MarkLengthText => InPoint is { } i && OutPoint is { } o && o > i ? ClipTime.Display(o - i) : "";

    // ---- Output ----

    [ObservableProperty]
    public partial double HandleSeconds { get; set; }

    [ObservableProperty]
    public partial CutModeOption SelectedCutMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsH264))]
    public partial ProfileOption SelectedProfile { get; set; }

    [ObservableProperty]
    public partial PresetOption SelectedQuality { get; set; }

    [ObservableProperty]
    public partial int? TargetFps { get; set; }

    [ObservableProperty]
    public partial int? TargetHeight { get; set; }

    [ObservableProperty]
    public partial bool ConstantFrameRate { get; set; }

    [ObservableProperty]
    public partial bool PcmAudio { get; set; }

    [ObservableProperty]
    public partial string EncoderText { get; private set; } = "Checking encoders…";

    [ObservableProperty]
    public partial double FrameEverySeconds { get; set; } = 10;

    [ObservableProperty]
    public partial string TimestampText { get; set; } = "";

    public bool IsH264 => SelectedProfile.Kind == EditorProfileKind.H264;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadClipsCommand))]
    public partial int ClipCount { get; private set; }

    public string DownloadClipsText => IsLocal ? $"Cut {ClipCount} clips" : $"Download {ClipCount} clips at full quality";

    /// <summary>Asks the shell to show the clip editor.</summary>
    public event EventHandler? ShowRequested;

    // ---- Opening sources ----

    /// <summary>Opens a video from Home: loads its saved clips and downloads (or reuses) a 360p proxy to mark them on.</summary>
    public async Task OpenUrlAsync(MediaInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var url = info.WebpageUrl ?? throw new ArgumentException("The video has no page URL.", nameof(info));

        await OpenSourceAsync(url, info.Title, isLocal: false, info);
        ShowRequested?.Invoke(this, EventArgs.Empty);

        var proxies = LibraryFolders.Inbox(_settings.Current, LibraryFolders.Proxies);
        if (FindProxy(proxies, info.Id) is { } existing)
        {
            LoadMedia(existing);
            return;
        }

        Status = "Downloading a 360p preview to mark clips on…";
        var options = new DownloadOptions
        {
            Url = url,
            OutputDirectory = proxies,
            Preset = FormatPreset.PreviewProxy,
            FilenameTemplate = "%(id)s proxy.%(ext)s",
            InfoJsonDirectory = _paths.InfoJson,
        };
        var added = await _queue.AddAsync([new NewJob(options, $"Proxy · {info.Title}")]);
        ProxyJob = _queueView.Upsert(added[0]);
        ProxyJob.PropertyChanged += OnProxyJobChanged;
    }

    [RelayCommand]
    private async Task OpenLocalFileAsync()
    {
        if (_dialogs.PickVideoFile("Open a video to clip") is { } path)
        {
            await OpenLocalPathAsync(path);
        }
    }

    /// <summary>Opens a video on disk, for example one passed on the command line ("Open with SSnip").</summary>
    public async Task OpenLocalPathAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await OpenSourceAsync(Path.GetFullPath(path), Path.GetFileNameWithoutExtension(path), isLocal: true, info: null);
        ShowRequested?.Invoke(this, EventArgs.Empty);
        LoadMedia(Path.GetFullPath(path));
    }

    private async Task OpenSourceAsync(string key, string title, bool isLocal, MediaInfo? info)
    {
        await SaveClipsAsync();
        StopReverse();
        Player.Stop();
        if (ProxyJob is not null)
        {
            ProxyJob.PropertyChanged -= OnProxyJobChanged;
            ProxyJob = null;
        }

        SourceKey = key;
        SourceTitle = title;
        IsLocal = isLocal;
        Info = info;
        MediaPath = null;
        Status = null;
        InPoint = null;
        OutPoint = null;
        PositionMs = 0;
        LengthMs = (long)(info?.Duration?.TotalMilliseconds ?? 0);
        _undo.Clear();

        _restoring = true;
        Clips.Clear();
        foreach (var clip in await _clipStore.LoadAsync(key))
        {
            AddRow(clip);
        }

        _restoring = false;
        OnPropertyChanged(nameof(DownloadClipsText));
        _ = DetectEncodersAsync();
    }

    private void OnProxyJobChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(JobViewModel.State) || sender is not JobViewModel job)
        {
            return;
        }

        if (job.State == JobState.Done && job.Job.FilePath is { } path)
        {
            job.PropertyChanged -= OnProxyJobChanged;
            ProxyJob = null;
            LoadMedia(path);
        }
        else if (job.State is JobState.Failed or JobState.Cancelled)
        {
            Status = job.State == JobState.Failed ? $"The preview download failed: {job.Job.Error}" : "The preview download was cancelled.";
        }
    }

    /// <summary>
    /// The page calls this once its video view holds the player. Playing before that makes LibVLC open
    /// a window of its own.
    /// </summary>
    public void OnVideoViewAttached() => _viewAttached.TrySetResult();

    private async void LoadMedia(string path)
    {
        MediaPath = path;
        Status = null;
        await _viewAttached.Task;
        using var media = new Media(_libVlc, new Uri(path));
        Player.Media = media;
        Player.Play();

        // Start paused on the first frame, ready to mark.
        await Task.Delay(300);
        Player.SetPause(true);
    }

    private static string? FindProxy(string folder, string id) =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder, $"{id} proxy.*").FirstOrDefault(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) : null;

    // ---- Playback commands ----

    [RelayCommand]
    private void PlayPause()
    {
        StopReverse();
        _speedIndex = 0;
        Player.SetRate(1);
        SpeedText = "";
        if (Player.IsPlaying)
        {
            Player.SetPause(true);
        }
        else
        {
            Player.Play();
        }
    }

    /// <summary>L: play forward; pressing again goes 2×, 4×, 8×.</summary>
    [RelayCommand]
    private void Forward()
    {
        if (_reverseIndex >= 0)
        {
            StopReverse();
            _speedIndex = 0;
        }
        else if (Player.IsPlaying)
        {
            _speedIndex = Math.Min(_speedIndex + 1, Speeds.Length - 1);
        }

        Player.SetRate(Speeds[_speedIndex]);
        Player.Play();
        SpeedText = _speedIndex == 0 ? "" : $"{Speeds[_speedIndex]:0}×";
    }

    /// <summary>J: play backwards; pressing again goes faster.</summary>
    [RelayCommand]
    private void Reverse()
    {
        Player.SetPause(true);
        _reverseIndex = Math.Min(_reverseIndex + 1, Speeds.Length - 1);
        SpeedText = $"◀ {Speeds[_reverseIndex]:0}×";
        _reverse.Start();
    }

    /// <summary>K: pause.</summary>
    [RelayCommand]
    private void Pause()
    {
        StopReverse();
        _speedIndex = 0;
        Player.SetRate(1);
        Player.SetPause(true);
        SpeedText = "";
    }

    [RelayCommand]
    private void StepForward()
    {
        StopReverse();
        Player.SetPause(true);

        // The next frame's time comes from the player, so stop holding the last seek target.
        _seekTarget = null;
        Player.NextFrame();
    }

    [RelayCommand]
    private void StepBack()
    {
        StopReverse();
        Player.SetPause(true);
        Seek(PositionMs - FrameMs());
    }

    [RelayCommand]
    private void SetIn()
    {
        InPoint = Now();
        if (OutPoint <= InPoint)
        {
            OutPoint = null;
        }
    }

    [RelayCommand]
    private void SetOut()
    {
        var now = Now();
        if (InPoint is { } i && now <= i)
        {
            Status = "The out point must come after the in point.";
            return;
        }

        OutPoint = now;
        Status = null;
    }

    /// <summary>Enter: adds the marked range as a clip.</summary>
    [RelayCommand(CanExecute = nameof(CanAddClip))]
    private void AddClip()
    {
        Remember();
        AddRow(new ClipMark { In = InPoint!.Value, Out = OutPoint!.Value, Name = $"Clip {Clips.Count + 1}" });
        SelectedClip = Clips[^1];
        InPoint = null;
        OutPoint = null;
    }

    private bool CanAddClip() => InPoint is { } i && OutPoint is { } o && o > i;

    /// <summary>Ctrl+Z: undoes the last change to the clip list.</summary>
    [RelayCommand]
    private void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _restoring = true;
        Clips.Clear();
        foreach (var clip in _undo.Pop())
        {
            AddRow(clip);
        }

        _restoring = false;
        OnClipsChanged();
    }

    [RelayCommand]
    private void DeleteClip(ClipRowViewModel? clip)
    {
        if (clip is not null)
        {
            Remember();
            Clips.Remove(clip);
        }
    }

    [RelayCommand]
    private void JumpTo(ClipRowViewModel? clip)
    {
        if (clip is not null)
        {
            Player.SetPause(true);
            Seek((long)clip.In.TotalMilliseconds);
        }
    }

    // ---- Importing clips ----

    /// <summary>Turns pasted lines such as "12:30-13:10 Soap scene" into clips.</summary>
    [RelayCommand]
    private void ImportTimestamps()
    {
        var clips = TimestampParser.ParseLines(TimestampText, Duration());
        if (clips.Count == 0)
        {
            Status = "No timestamps found. Use lines like 12:30-13:10 Soap scene.";
            return;
        }

        Remember();
        foreach (var clip in clips)
        {
            AddRow(clip);
        }

        TimestampText = "";
        Status = $"Added {clips.Count} clips from the pasted lines.";
    }

    [RelayCommand]
    private void ScanDescription()
    {
        var clips = TimestampParser.ScanDescription(Info?.Description, Duration());
        Status = clips.Count == 0 ? "The description has no timestamps." : $"Found {clips.Count} timestamps in the description.";
        if (clips.Count > 0)
        {
            Remember();
            foreach (var clip in clips)
            {
                AddRow(clip);
            }
        }
    }

    [RelayCommand]
    private void AddChapters()
    {
        if (Info is null || Info.Chapters.Count == 0)
        {
            return;
        }

        Remember();
        foreach (var chapter in Info.Chapters)
        {
            AddRow(new ClipMark { In = chapter.Start, Out = chapter.End, Name = chapter.Title });
        }
    }

    /// <summary>Adds clips from outside the editor, for example ticked chapters on Home.</summary>
    public void AddClips(IEnumerable<ClipMark> clips)
    {
        Remember();
        foreach (var clip in clips)
        {
            AddRow(clip);
        }
    }

    // ---- Output ----

    [RelayCommand(CanExecute = nameof(HasClips))]
    private async Task DownloadClipsAsync()
    {
        await SaveClipsAsync();
        var batch = new ClipBatch(
            LibraryFolders.Inbox(_settings.Current, LibraryFolders.Clips),
            TimeSpan.FromSeconds(Math.Clamp(HandleSeconds, 0, ClipTime.MaxHandleSeconds)),
            SelectedCutMode.Mode,
            Profile(),
            SelectedQuality.Preset,
            _paths.InfoJson);

        var clips = Clips.Select(c => c.ToMark()).ToList();
        var jobs = IsLocal
            ? ClipJobBuilder.ForLocalFile(SourceKey!, clips, Duration(), batch)
            : ClipJobBuilder.ForUrl(SourceKey!, SourceTitle, clips, Duration(), batch);
        await _queue.AddAsync(jobs);
        Status = $"Added {jobs.Count} clips to the queue.";
        await SaveDefaultsAsync();
    }

    private bool HasClips() => ClipCount > 0 && SourceKey is not null;

    /// <summary>Saves the current frame as a full-resolution PNG: from the best stream for a URL, not the proxy.</summary>
    [RelayCommand]
    private async Task GrabFrameAsync()
    {
        if (SourceKey is null)
        {
            return;
        }

        await AddFramesJobAsync(new FrameOptions
        {
            Input = SourceKey,
            OutputDirectory = LibraryFolders.Inbox(_settings.Current, LibraryFolders.Frames),
            Name = SourceTitle,
            Mode = FrameMode.Single,
            At = Now(),
        });
    }

    /// <summary>One frame every N seconds from the file being played (the proxy for a URL).</summary>
    [RelayCommand(CanExecute = nameof(HasMedia))]
    private Task GrabFramesEveryAsync() => AddFramesJobAsync(new FrameOptions
    {
        Input = MediaPath!,
        OutputDirectory = LibraryFolders.Inbox(_settings.Current, LibraryFolders.Frames),
        Name = SourceTitle,
        Mode = FrameMode.Every,
        EverySeconds = Math.Max(0.5, FrameEverySeconds),
        Duration = Duration(),
    });

    [RelayCommand(CanExecute = nameof(HasMedia))]
    private Task ContactSheetAsync() => AddFramesJobAsync(new FrameOptions
    {
        Input = MediaPath!,
        OutputDirectory = LibraryFolders.Inbox(_settings.Current, LibraryFolders.Frames),
        Name = SourceTitle,
        Mode = FrameMode.ContactSheet,
        EverySeconds = Math.Max(0.5, FrameEverySeconds),
        Duration = Duration(),
    });

    [RelayCommand]
    private async Task SaveThumbnailAsync()
    {
        if (Info?.ThumbnailUrl is not { } url)
        {
            Status = "This source has no thumbnail.";
            return;
        }

        try
        {
            var path = await _thumbnails.SaveAsync(url, LibraryFolders.Inbox(_settings.Current, LibraryFolders.Frames), SourceTitle);
            Status = $"Saved the thumbnail: {path}";
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or TaskCanceledException)
        {
            Status = $"Could not save the thumbnail: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenFolder(string kind) => _dialogs.OpenInExplorer(LibraryFolders.Inbox(_settings.Current, kind));

    // ---- Helpers ----

    /// <summary>
    /// Saves the clips after every change, when the page is left and when another source opens.
    /// Saves run one at a time, so an older list never lands after a newer one.
    /// </summary>
    public async Task SaveClipsAsync()
    {
        if (SourceKey is not { } source)
        {
            return;
        }

        IReadOnlyList<ClipMark> clips = [.. Clips.Select(c => c.ToMark())];
        await _saveGate.WaitAsync();
        try
        {
            await _clipStore.SaveAsync(source, clips);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            _logger.LogError(ex, "Could not save the clips for {Source}", source);
            Status = "Could not save the clips; they stay in the editor until you close the app.";
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public void Dispose()
    {
        _reverse.Stop();
        Player.Dispose();
        _libVlc.Dispose();
    }

    partial void OnPositionMsChanged(long value)
    {
        if (!_positionFromPlayer && Player.Media is not null)
        {
            _seekTarget = value;
            Player.Time = value;
        }
    }

    private void SetPositionFromPlayer(long time)
    {
        // After a seek while paused, LibVLC reports a time a few milliseconds off; keep the exact target.
        if (!Player.IsPlaying && _seekTarget is { } target && Math.Abs(time - target) < 250)
        {
            return;
        }

        if (Player.IsPlaying)
        {
            _seekTarget = null;
        }

        _positionFromPlayer = true;
        PositionMs = time;
        _positionFromPlayer = false;
    }

    private void Seek(long ms)
    {
        var clamped = Math.Clamp(ms, 0, LengthMs > 0 ? LengthMs : long.MaxValue);
        if (clamped == 0 && _reverseIndex >= 0)
        {
            StopReverse();
        }

        PositionMs = clamped;
    }

    private void StopReverse()
    {
        _reverse.Stop();
        _reverseIndex = -1;
    }

    /// <summary>
    /// The current time to the millisecond. While paused it is the position last seeked or stepped to:
    /// LibVLC's own time lags a seek by a few milliseconds.
    /// </summary>
    private TimeSpan Now() => TimeSpan.FromMilliseconds(Player.Media is null || !Player.IsPlaying ? PositionMs : Math.Max(0, Player.Time));

    private long FrameMs()
    {
        var fps = Player.Fps > 1 ? Player.Fps : 30;
        return (long)Math.Round(1000 / fps);
    }

    private TimeSpan? Duration() => LengthMs > 0 ? TimeSpan.FromMilliseconds(LengthMs) : Info?.Duration;

    private void Remember() => _undo.Push([.. Clips.Select(c => c.ToMark())]);

    private void AddRow(ClipMark clip)
    {
        var row = new ClipRowViewModel(clip);
        row.PropertyChanged += (_, _) => OnClipsChanged();
        Clips.Add(row);
    }

    private void OnClipsChanged()
    {
        ClipCount = Clips.Count;
        OnPropertyChanged(nameof(DownloadClipsText));
        if (!_restoring)
        {
            _ = SaveClipsAsync();
        }
    }

    private EditorProfile Profile() => new(
        SelectedProfile.Kind,
        SelectedProfile.Kind == EditorProfileKind.H264 ? _settings.Current.Clipping.H264Encoder ?? _encoders?.H264Encoder : null,
        TargetFps,
        TargetHeight,
        ConstantFrameRate,
        PcmAudio ? ProfileAudio.Pcm48k : ProfileAudio.Aac48k);

    private async Task AddFramesJobAsync(FrameOptions options)
    {
        await _queue.AddAsync([new NewJob(options, $"{options.Mode switch { FrameMode.Every => "Frames", FrameMode.ContactSheet => "Contact sheet", _ => "Frame" }} · {SourceTitle}")]);
        Status = options.Mode == FrameMode.Single
            ? $"Grabbing the frame at {ClipTime.Display(options.At)} at full quality; it lands in Inbox\\frames."
            : "Added to the queue; the images land in Inbox\\frames.";
    }

    private async Task DetectEncodersAsync()
    {
        try
        {
            _encoders = await _ffmpeg.DetectEncodersAsync();
            EncoderText = _encoders.WorkingHardware.Count > 0
                ? $"H.264 uses {_encoders.H264Encoder} (GPU)"
                : "No working GPU encoder; H.264 uses libx264 -crf 16";
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
        {
            EncoderText = "ffmpeg is not installed.";
            _logger.LogWarning(ex, "Encoder detection failed");
        }
    }

    private Task SaveDefaultsAsync()
    {
        var clipping = _settings.Current.Clipping with
        {
            HandleSeconds = HandleSeconds,
            CutMode = SelectedCutMode.Mode,
            Profile = SelectedProfile.Kind,
            TargetFps = TargetFps,
            TargetHeight = TargetHeight,
            ConstantFrameRate = ConstantFrameRate,
            Audio = PcmAudio ? ProfileAudio.Pcm48k : ProfileAudio.Aac48k,
        };
        return clipping == _settings.Current.Clipping ? Task.CompletedTask : _settings.SaveAsync(_settings.Current with { Clipping = clipping });
    }
}
