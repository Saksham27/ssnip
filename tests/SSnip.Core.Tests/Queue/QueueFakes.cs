using System.Collections.Concurrent;
using SSnip.Core.Downloads;
using SSnip.Core.Queue;

namespace SSnip.Core.Tests.Queue;

internal sealed class InMemoryJobStore : IJobStore
{
    private readonly ConcurrentDictionary<long, QueueJob> _jobs = new();
    private long _nextId;

    public IReadOnlyList<QueueJob> Stored => [.. _jobs.Values.OrderBy(j => j.Id)];

    public Task<IReadOnlyList<QueueJob>> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<QueueJob>>([.. _jobs.Values]);

    public Task<QueueJob> AddAsync(QueueJob job, CancellationToken cancellationToken = default)
    {
        var stored = job with { Id = Interlocked.Increment(ref _nextId) };
        _jobs[stored.Id] = stored;
        return Task.FromResult(stored);
    }

    public Task UpdateAsync(QueueJob job, CancellationToken cancellationToken = default)
    {
        // Like the database, the store never holds live progress.
        _jobs[job.Id] = job with { Progress = null };
        return Task.CompletedTask;
    }

    public Task RemoveAsync(long id, CancellationToken cancellationToken = default)
    {
        _jobs.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}

/// <summary>Runs each job until the test finishes it, and records how many ran at once.</summary>
internal sealed class ControlledExecutor : IJobExecutor
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JobOutcome>> _pending = new();
    private int _running;
    private int _maxRunning;

    public ConcurrentQueue<QueueJob> Started { get; } = new();

    public int MaxRunning => Volatile.Read(ref _maxRunning);

    public int Running => Volatile.Read(ref _running);

    public async Task<JobOutcome> RunAsync(QueueJob job, IProgress<JobUpdate> updates, CancellationToken abort, CancellationToken stop)
    {
        var running = Interlocked.Increment(ref _running);
        InterlockedMax(ref _maxRunning, running);
        Started.Enqueue(job);
        try
        {
            updates.Report(new JobUpdate(Progress: new JobProgress(DownloadStage.Video, 0.5), Title: "Title of " + job.Options.Url));
            var outcome = Completion(job.Options.Url);
            using (stop.Register(() => outcome.TrySetResult(JobOutcome.Done("recorded.ts", "Recording stopped"))))
            {
                return await outcome.Task.WaitAsync(abort);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _running);
            _pending.TryRemove(job.Options.Url, out _);
        }
    }

    public void Finish(string url, JobOutcome? outcome = null) =>
        Completion(url).TrySetResult(outcome ?? JobOutcome.Done($@"E:\raw\{url}.mp4"));

    public void Throw(string url, Exception exception) => Completion(url).TrySetException(exception);

    private TaskCompletionSource<JobOutcome> Completion(string url) =>
        _pending.GetOrAdd(url, _ => new TaskCompletionSource<JobOutcome>(TaskCreationOptions.RunContinuationsAsynchronously));

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
