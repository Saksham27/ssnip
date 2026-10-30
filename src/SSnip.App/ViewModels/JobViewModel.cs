using CommunityToolkit.Mvvm.ComponentModel;
using SSnip.Core.Clipping;
using SSnip.Core.Downloads;
using SSnip.Core.Formatting;
using SSnip.Core.Queue;

namespace SSnip.App.ViewModels;

/// <summary>One queue row, also used for the progress card on Home.</summary>
public sealed partial class JobViewModel : ObservableObject
{
    public JobViewModel(QueueJob job)
    {
        Job = job;
        Apply(job);
    }

    public long Id => Job.Id;

    [ObservableProperty]
    public partial QueueJob Job { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    [ObservableProperty]
    public partial JobState State { get; private set; }

    [ObservableProperty]
    public partial string StageText { get; private set; } = "";

    [ObservableProperty]
    public partial double ProgressValue { get; private set; }

    [ObservableProperty]
    public partial bool IsIndeterminate { get; private set; }

    [ObservableProperty]
    public partial string PercentText { get; private set; } = "";

    [ObservableProperty]
    public partial string SizeText { get; private set; } = "";

    [ObservableProperty]
    public partial string SpeedText { get; private set; } = "";

    [ObservableProperty]
    public partial string EtaText { get; private set; } = "";

    /// <summary>Error, note or final path, whichever matters most.</summary>
    [ObservableProperty]
    public partial string? Detail { get; private set; }

    [ObservableProperty]
    public partial bool IsActive { get; private set; }

    [ObservableProperty]
    public partial bool IsFailed { get; private set; }

    [ObservableProperty]
    public partial bool IsDone { get; private set; }

    [ObservableProperty]
    public partial bool CanPause { get; private set; }

    [ObservableProperty]
    public partial bool CanResume { get; private set; }

    [ObservableProperty]
    public partial bool CanCancel { get; private set; }

    [ObservableProperty]
    public partial bool CanRetry { get; private set; }

    [ObservableProperty]
    public partial bool CanStopRecording { get; private set; }

    [ObservableProperty]
    public partial bool HasFile { get; private set; }

    public string PresetText => Job.Options switch
    {
        DownloadOptions download => (download.Preset switch
        {
            FormatPreset.EditorSafe => "Editor-safe",
            FormatPreset.MaxQuality => "Max quality",
            FormatPreset.Cap1080 => "1080p cap",
            FormatPreset.AudioOnly => $"Audio · {download.AudioFormat.ToString().ToUpperInvariant()}",
            FormatPreset.PreviewProxy => "Preview proxy",
            _ => "Custom",
        })
            + (download.Sections.Count > 0 ? $" · clip, {(download.CutMode == CutMode.Accurate ? "accurate" : "fast")}" : "")
            + (download.Profile is { Converts: true } profile ? $" · {profile.Kind}" : "")
            + (Job.IsLive ? " · live" : ""),
        LocalCutOptions cut => $"Local cut, {(cut.Mode == CutMode.Accurate ? "accurate" : "fast")}",
        FrameOptions frames => frames.Mode switch
        {
            FrameMode.Every => $"Frames every {frames.EverySeconds:0.#} s",
            FrameMode.ContactSheet => "Contact sheet",
            _ => "Frame grab",
        },
        _ => "",
    };

    /// <summary>Takes a newer copy of the job; an older one that arrives late is ignored.</summary>
    public bool Update(QueueJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Revision < Job.Revision)
        {
            return false;
        }

        Job = job;
        Apply(job);
        return true;
    }

    private void Apply(QueueJob job)
    {
        Title = job.Title;
        State = job.State;
        IsActive = job.IsActive;
        IsFailed = job.State == JobState.Failed;
        IsDone = job.State == JobState.Done;
        CanPause = job.IsActive || job.State == JobState.Queued;
        CanResume = job.State == JobState.Paused;
        CanCancel = job.IsActive || job.State is JobState.Queued or JobState.Paused;
        CanRetry = job.IsFinished;
        CanStopRecording = job.IsActive && job.IsLive;
        HasFile = job.FilePath is not null;

        var progress = job.Progress;
        var fraction = progress?.Fraction ?? job.Fraction;
        StageText = job.State switch
        {
            JobState.Queued => job.ResumePartial ? "Queued, continues where it stopped" : "Queued",
            JobState.Paused => fraction is { } f ? $"Paused at {Display.Percent(f)}" : "Paused",
            JobState.Done => job.Note ?? (job.StartedAt is { } start && job.FinishedAt is { } end ? $"Done in {Display.Duration(end - start)}" : "Done"),
            JobState.Failed => "Failed",
            JobState.Cancelled => "Cancelled; partial files are kept",
            _ => DownloadStageResolver.Describe(progress?.Stage ?? DownloadStage.Starting),
        };

        Detail = job.State switch
        {
            JobState.Failed => job.Error,
            JobState.Done when job.Note is not null => job.FilePath,
            JobState.Done => job.FilePath,
            _ => null,
        };

        IsIndeterminate = job.IsActive && (progress?.Fraction is null || job.State == JobState.PostProcessing);
        ProgressValue = job.State == JobState.Done ? 100 : (fraction ?? 0) * 100;
        PercentText = job.State == JobState.Done ? "100%" : progress?.Fraction is { } p ? Display.Percent(p) : progress?.FragmentCount is { } count ? $"fragment {progress.FragmentIndex}/{count}" : "";

        if (job.IsActive && progress is not null && progress.DownloadedBytes > 0)
        {
            SizeText = progress.TotalBytes is { } total
                ? $"{Display.Bytes(progress.DownloadedBytes)} of {(progress.IsTotalEstimate ? "~" : "")}{Display.Bytes(total)}"
                : Display.Bytes(progress.DownloadedBytes);
        }
        else if (!job.IsActive)
        {
            SizeText = job.State == JobState.Done && job.FilePath is not null && System.IO.File.Exists(job.FilePath)
                ? Display.Bytes(new System.IO.FileInfo(job.FilePath).Length)
                : "";
        }

        SpeedText = job.IsActive && progress?.BytesPerSecond is { } speed ? Display.Speed(speed) : "";
        EtaText = job.IsActive && progress?.Eta is { } eta ? $"ETA {Display.Duration(eta)}" : "";
    }
}
