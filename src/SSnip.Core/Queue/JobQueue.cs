using System.Threading.Channels;
using SSnip.Core.Downloads;
using SSnip.Core.Processes;

namespace SSnip.Core.Queue;

/// <summary>
/// The persistent download queue: runs up to <see cref="Concurrency"/> jobs at once in queue order,
/// with pause, resume, cancel, retry, reorder and remove. Every state change is written to the
/// <see cref="IJobStore"/> in order on one background writer, so the queue survives closing the app;
/// live progress is reported through <see cref="JobChanged"/> but not stored.
/// </summary>
public sealed class JobQueue : IAsyncDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly IJobStore _store;
    private readonly IJobExecutor _executor;
    private readonly TimeProvider _time;
    private readonly object _sync = new();
    private readonly Dictionary<long, QueueJob> _jobs = [];
    private readonly Dictionary<long, Run> _runs = [];
    private readonly HashSet<long> _batch = [];
    private readonly Channel<StoreWrite> _writes = Channel.CreateUnbounded<StoreWrite>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _writer;
    private int _concurrency = JobScheduler.DefaultConcurrency;
    private bool _loaded;
    private bool _shuttingDown;

    public JobQueue(IJobStore store, IJobExecutor executor, TimeProvider? time = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _time = time ?? TimeProvider.System;
        _writer = Task.Run(WriteLoopAsync);
    }

    /// <summary>A job was added or changed. Raised on whichever thread made the change.</summary>
    public event EventHandler<QueueJob>? JobChanged;

    public event EventHandler<long>? JobRemoved;

    /// <summary>The last job finished and nothing is queued, running or paused.</summary>
    public event EventHandler? Finished;

    /// <summary>Writing to the store failed; the queue keeps running in memory.</summary>
    public event EventHandler<Exception>? StoreFailed;

    public IReadOnlyList<QueueJob> Jobs
    {
        get
        {
            lock (_sync)
            {
                return [.. JobScheduler.InOrder(_jobs.Values)];
            }
        }
    }

    public QueueSummary Summary
    {
        get
        {
            lock (_sync)
            {
                return JobScheduler.Summarize(_jobs.Values, _batch);
            }
        }
    }

    public int Concurrency
    {
        get
        {
            lock (_sync)
            {
                return _concurrency;
            }
        }
    }

    public QueueJob? Find(long id)
    {
        lock (_sync)
        {
            return _jobs.GetValueOrDefault(id);
        }
    }

    /// <summary>
    /// Loads the stored queue and starts it. Jobs that were running when the app closed go back to
    /// Queued and continue their partial files.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var resumed = new List<QueueJob>();
        lock (_sync)
        {
            foreach (var job in stored)
            {
                var current = job.IsActive ? job with { State = JobState.Queued, ResumePartial = true } : job;
                if (current != job)
                {
                    resumed.Add(current);
                }

                _jobs[current.Id] = current;
                if (current.State == JobState.Queued)
                {
                    _batch.Add(current.Id);
                }
            }

            _loaded = true;
        }

        resumed.ForEach(Save);
        Pump();
    }

    public async Task<IReadOnlyList<QueueJob>> AddAsync(IEnumerable<NewJob> jobs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        int position;
        lock (_sync)
        {
            position = JobScheduler.NextPosition(_jobs.Values);
        }

        var added = new List<QueueJob>();
        foreach (var newJob in jobs)
        {
            var job = new QueueJob
            {
                Options = newJob.Options,
                Title = string.IsNullOrWhiteSpace(newJob.Title) ? newJob.Options.Url : newJob.Title,
                Position = position++,
                CreatedAt = _time.GetUtcNow(),
            };

            var completion = new TaskCompletionSource<QueueJob>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _writes.Writer.WriteAsync(new StoreWrite.Add(job, completion), cancellationToken).ConfigureAwait(false);
            var stored = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            lock (_sync)
            {
                _jobs[stored.Id] = stored;
                _batch.Add(stored.Id);
            }

            added.Add(stored);
            JobChanged?.Invoke(this, stored);
        }

        Pump();
        return added;
    }

    /// <summary>Pauses a running or queued job. A running one is stopped and keeps its partial files.</summary>
    public void Pause(long id) => Interrupt(id, JobState.Paused, job => job.State == JobState.Queued);

    /// <summary>Cancels a running, queued or paused job; partial files are kept.</summary>
    public void Cancel(long id) => Interrupt(id, JobState.Cancelled, job => job.State is JobState.Queued or JobState.Paused);

    public void Resume(long id) => Requeue(id, job => job.State == JobState.Paused);

    /// <summary>Runs a failed, cancelled or finished job again.</summary>
    public void Retry(long id) => Requeue(id, job => job.IsFinished);

    public void PauseAll()
    {
        foreach (var job in Jobs.Where(j => j.IsActive || j.State == JobState.Queued))
        {
            Pause(job.Id);
        }
    }

    public void ResumeAll()
    {
        foreach (var job in Jobs.Where(j => j.State == JobState.Paused))
        {
            Resume(job.Id);
        }
    }

    /// <summary>Asks a live recording to stop and keep what it recorded.</summary>
    public void StopRecording(long id)
    {
        Run? run;
        lock (_sync)
        {
            run = _jobs.TryGetValue(id, out var job) && job.IsLive ? _runs.GetValueOrDefault(id) : null;
        }

        run?.Stop.Cancel();
    }

    public void Remove(long id)
    {
        Run? run;
        lock (_sync)
        {
            if (!_jobs.Remove(id))
            {
                return;
            }

            _batch.Remove(id);
            run = _runs.GetValueOrDefault(id);
        }

        run?.Abort.Cancel();
        _writes.Writer.TryWrite(new StoreWrite.Remove(id));
        JobRemoved?.Invoke(this, id);
        Pump();
    }

    /// <summary>Removes every Done, Failed and Cancelled job. Files on disk are not touched.</summary>
    public int ClearFinished()
    {
        var finished = Jobs.Where(j => j.IsFinished).ToList();
        finished.ForEach(j => Remove(j.Id));
        return finished.Count;
    }

    /// <summary>Moves a job up (negative) or down in the queue.</summary>
    public void Move(long id, int offset)
    {
        IReadOnlyList<QueueJob> moved;
        lock (_sync)
        {
            moved = JobScheduler.Move(_jobs.Values, id, offset);
            foreach (var job in moved)
            {
                _jobs[job.Id] = job;
            }
        }

        foreach (var job in moved)
        {
            Save(job);
            JobChanged?.Invoke(this, job);
        }
    }

    public void SetConcurrency(int concurrency)
    {
        lock (_sync)
        {
            _concurrency = Math.Clamp(concurrency, JobScheduler.MinConcurrency, JobScheduler.MaxConcurrency);
        }

        Pump();
    }

    /// <summary>Waits until every change so far is in the store.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_writes.Writer.TryWrite(new StoreWrite.Flush(flushed)))
        {
            await flushed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stops running jobs for an app exit. They are stored as Queued, so the next start continues
    /// their partial files.
    /// </summary>
    public async Task ShutdownAsync()
    {
        List<Run> runs;
        lock (_sync)
        {
            _shuttingDown = true;
            runs = [.. _runs.Values];
            runs.ForEach(r => r.Intent = JobState.Queued);
        }

        runs.ForEach(r => r.Abort.Cancel());
        try
        {
            await Task.WhenAll(runs.Select(r => r.Task)).WaitAsync(ShutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Store whatever state there is; the processes are already being killed.
        }

        await FlushAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_shuttingDown)
        {
            await ShutdownAsync().ConfigureAwait(false);
        }

        _writes.Writer.TryComplete();
        await _writer.ConfigureAwait(false);
    }

    private void Interrupt(long id, JobState intent, Func<QueueJob, bool> canChangeDirectly)
    {
        Run? run = null;
        QueueJob? changed = null;
        lock (_sync)
        {
            if (!_jobs.TryGetValue(id, out var job))
            {
                return;
            }

            if (_runs.TryGetValue(id, out run))
            {
                run.Intent = intent;
            }
            else if (canChangeDirectly(job))
            {
                changed = job with
                {
                    State = intent,
                    FinishedAt = intent == JobState.Cancelled ? _time.GetUtcNow() : job.FinishedAt,
                };
                _jobs[id] = changed;
            }
        }

        // Cancelling kills the process tree, which can take a moment, so it happens outside the lock.
        run?.Abort.Cancel();
        if (changed is not null)
        {
            Save(changed);
            JobChanged?.Invoke(this, changed);
            CheckFinished();
        }
    }

    private void Requeue(long id, Func<QueueJob, bool> allowed)
    {
        QueueJob? changed = null;
        lock (_sync)
        {
            if (_jobs.TryGetValue(id, out var job) && !_runs.ContainsKey(id) && allowed(job))
            {
                changed = job with
                {
                    State = JobState.Queued,
                    Error = null,
                    Note = null,
                    FilePath = job.State == JobState.Done ? null : job.FilePath,
                    Fraction = job.State == JobState.Done ? null : job.Fraction,
                    FinishedAt = null,
                    ResumePartial = job.State != JobState.Done && job.ResumePartial,
                };
                _jobs[id] = changed;
                _batch.Add(id);
            }
        }

        if (changed is not null)
        {
            Save(changed);
            JobChanged?.Invoke(this, changed);
            Pump();
        }
    }

    /// <summary>Starts queued jobs while there is room.</summary>
    private void Pump()
    {
        var started = new List<(QueueJob Job, Run Run)>();
        lock (_sync)
        {
            if (!_loaded || _shuttingDown)
            {
                return;
            }

            foreach (var job in JobScheduler.NextToStart(_jobs.Values, _concurrency))
            {
                var running = job with
                {
                    State = JobState.Downloading,
                    StartedAt = _time.GetUtcNow(),
                    Error = null,
                    Note = null,
                    Progress = new JobProgress(DownloadStage.Starting),
                };
                var run = new Run();
                _jobs[job.Id] = running;
                _runs[job.Id] = run;
                started.Add((running, run));
            }
        }

        foreach (var (job, run) in started)
        {
            Save(job);
            JobChanged?.Invoke(this, job);
            run.Task = Task.Run(() => ExecuteAsync(job, run));
        }
    }

    private async Task ExecuteAsync(QueueJob job, Run run)
    {
        var updates = new SynchronousProgress<JobUpdate>(update => Apply(job.Id, update));
        JobOutcome? outcome = null;
        try
        {
            outcome = await _executor.RunAsync(job, updates, run.Abort.Token, run.Stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (run.Abort.IsCancellationRequested)
        {
            // Paused, cancelled, removed or the app is closing; Intent says which.
        }
        catch (Exception ex)
        {
            outcome = JobOutcome.Failed(ex.Message);
        }

        Complete(job.Id, run, outcome);
        run.Abort.Dispose();
        run.Stop.Dispose();
    }

    private void Apply(long id, JobUpdate update)
    {
        QueueJob next;
        bool store;
        lock (_sync)
        {
            if (!_jobs.TryGetValue(id, out var job) || !_runs.ContainsKey(id))
            {
                return;
            }

            var state = update.State is { } s && s is JobState.Fetching or JobState.Downloading or JobState.PostProcessing ? s : job.State;
            next = job with
            {
                State = state,
                Progress = update.Progress ?? job.Progress,
                Title = update.Title ?? job.Title,
                CommandLine = update.CommandLine ?? job.CommandLine,
                LogPath = update.LogPath ?? job.LogPath,
            };

            store = next.State != job.State || next.Title != job.Title || next.CommandLine != job.CommandLine || next.LogPath != job.LogPath;
            _jobs[id] = next;
        }

        if (store)
        {
            Save(next);
        }

        JobChanged?.Invoke(this, next);
    }

    private void Complete(long id, Run run, JobOutcome? outcome)
    {
        QueueJob? next = null;
        lock (_sync)
        {
            _runs.Remove(id);
            if (_jobs.TryGetValue(id, out var current))
            {
                var fraction = current.Progress?.Fraction ?? current.Fraction;
                next = outcome is null
                    ? current with
                    {
                        State = run.Intent,
                        Progress = null,
                        Fraction = fraction,
                        ResumePartial = true,
                        FinishedAt = run.Intent == JobState.Cancelled ? _time.GetUtcNow() : null,
                    }
                    : current with
                    {
                        State = outcome.State,
                        FilePath = outcome.FilePath,
                        Error = outcome.Error,
                        Note = outcome.Note,
                        CommandLine = outcome.CommandLine ?? current.CommandLine,
                        Progress = null,
                        Fraction = outcome.State == JobState.Done ? 1 : fraction,
                        ResumePartial = outcome.State != JobState.Done,
                        FinishedAt = _time.GetUtcNow(),
                    };
                _jobs[id] = next;
            }
        }

        if (next is not null)
        {
            Save(next);
            JobChanged?.Invoke(this, next);
        }

        Pump();
        CheckFinished();
    }

    private void CheckFinished()
    {
        lock (_sync)
        {
            if (_shuttingDown || _batch.Count == 0 || !JobScheduler.IsFinished(_jobs.Values))
            {
                return;
            }

            _batch.Clear();
        }

        Finished?.Invoke(this, EventArgs.Empty);
    }

    private void Save(QueueJob job) => _writes.Writer.TryWrite(new StoreWrite.Update(job));

    private async Task WriteLoopAsync()
    {
        await foreach (var write in _writes.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                switch (write)
                {
                    case StoreWrite.Add add:
                        add.Completion.TrySetResult(await _store.AddAsync(add.Job).ConfigureAwait(false));
                        break;
                    case StoreWrite.Update update:
                        await _store.UpdateAsync(update.Job).ConfigureAwait(false);
                        break;
                    case StoreWrite.Remove remove:
                        await _store.RemoveAsync(remove.Id).ConfigureAwait(false);
                        break;
                    case StoreWrite.Flush flush:
                        flush.Completion.TrySetResult();
                        break;
                }
            }
            catch (Exception ex)
            {
                (write as StoreWrite.Add)?.Completion.TrySetException(ex);
                StoreFailed?.Invoke(this, ex);
            }
        }
    }

    private sealed class Run
    {
        public CancellationTokenSource Abort { get; } = new();

        public CancellationTokenSource Stop { get; } = new();

        /// <summary>The state the job takes when it is aborted.</summary>
        public JobState Intent { get; set; } = JobState.Cancelled;

        public Task Task { get; set; } = Task.CompletedTask;
    }

    private abstract record StoreWrite
    {
        public sealed record Add(QueueJob Job, TaskCompletionSource<QueueJob> Completion) : StoreWrite;

        public sealed record Update(QueueJob Job) : StoreWrite;

        public sealed record Remove(long Id) : StoreWrite;

        public sealed record Flush(TaskCompletionSource Completion) : StoreWrite;
    }
}
