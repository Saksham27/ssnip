namespace SSnip.Core.Queue;

/// <summary>Where the queue is kept between runs (SQLite in the app).</summary>
public interface IJobStore
{
    Task<IReadOnlyList<QueueJob>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores a new job and returns it with its id.</summary>
    Task<QueueJob> AddAsync(QueueJob job, CancellationToken cancellationToken = default);

    Task UpdateAsync(QueueJob job, CancellationToken cancellationToken = default);

    Task RemoveAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>Runs one job, for example a yt-dlp download.</summary>
public interface IJobExecutor
{
    /// <summary>
    /// Runs <paramref name="job"/> to the end. <paramref name="abort"/> kills it and throws
    /// <see cref="OperationCanceledException"/>, keeping partial files; <paramref name="stop"/> asks a live
    /// recording to finish its file and return normally.
    /// </summary>
    Task<JobOutcome> RunAsync(QueueJob job, IProgress<JobUpdate> updates, CancellationToken abort, CancellationToken stop);
}
