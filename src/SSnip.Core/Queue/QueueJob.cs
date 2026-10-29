using SSnip.Core.Downloads;

namespace SSnip.Core.Queue;

public enum JobKind
{
    Download,
}

/// <summary>
/// One job in the persistent queue. Immutable: <see cref="JobQueue"/> replaces it on every change,
/// so a copy handed to the UI never changes under it.
/// </summary>
public sealed record QueueJob
{
    public long Id { get; init; }

    public JobKind Kind { get; init; } = JobKind.Download;

    public required DownloadOptions Options { get; init; }

    /// <summary>The video title once known; the URL until then.</summary>
    public required string Title { get; init; }

    public JobState State { get; init; } = JobState.Queued;

    /// <summary>Order in the queue; lower runs first.</summary>
    public int Position { get; init; }

    public string? CommandLine { get; init; }

    public string? FilePath { get; init; }

    public string? Error { get; init; }

    /// <summary>Something worth knowing about a finished job, for example that the file already existed.</summary>
    public string? Note { get; init; }

    public string? LogPath { get; init; }

    /// <summary>Last known progress, 0 to 1; kept so a paused job shows how far it got.</summary>
    public double? Fraction { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>
    /// A previous run was interrupted and left <c>.part</c> files, so the next run continues them
    /// instead of overwriting.
    /// </summary>
    public bool ResumePartial { get; init; }

    /// <summary>Live progress while running. Not stored.</summary>
    public JobProgress? Progress { get; init; }

    public bool IsLive => Options.Live is not null;

    public bool IsActive => State is JobState.Fetching or JobState.Downloading or JobState.PostProcessing;

    public bool IsFinished => State is JobState.Done or JobState.Failed or JobState.Cancelled;
}

/// <summary>Live progress of a running job.</summary>
public sealed record JobProgress(
    DownloadStage Stage,
    double? Fraction = null,
    long DownloadedBytes = 0,
    long? TotalBytes = null,
    bool IsTotalEstimate = false,
    double? BytesPerSecond = null,
    TimeSpan? Eta = null,
    int? FragmentIndex = null,
    int? FragmentCount = null);

/// <summary>A job to add; the title defaults to the URL until yt-dlp reports it.</summary>
public sealed record NewJob(DownloadOptions Options, string? Title = null);

/// <summary>Something a running job reports. Null fields are unchanged.</summary>
public sealed record JobUpdate(
    JobState? State = null,
    JobProgress? Progress = null,
    string? Title = null,
    string? CommandLine = null,
    string? LogPath = null);

/// <summary>How a job run ended, when it was not aborted.</summary>
public sealed record JobOutcome(JobState State, string? FilePath = null, string? Error = null, string? Note = null, string? CommandLine = null)
{
    public static JobOutcome Done(string? filePath, string? note = null, string? commandLine = null) => new(JobState.Done, filePath, null, note, commandLine);

    public static JobOutcome Failed(string error, string? commandLine = null) => new(JobState.Failed, null, error, null, commandLine);
}

/// <summary>What to do when the last job in the queue finishes.</summary>
public enum WhenQueueFinishes
{
    DoNothing,
    Sleep,
    ShutDown,
}
