using SSnip.Core.Downloads;

namespace SSnip.Core.Queue;

/// <summary>Counts for the status bar and the taskbar.</summary>
/// <param name="Fraction">Overall progress of the jobs in this run, for taskbar progress; null when idle.</param>
public sealed record QueueSummary(int Running, int Queued, int Paused, int Failed, int Done, double? Fraction)
{
    public bool IsIdle => Running == 0 && Queued == 0;

    public override string ToString()
    {
        var text = $"{Running} running, {Queued} queued";
        if (Paused > 0)
        {
            text += $", {Paused} paused";
        }

        return Failed > 0 ? $"{text}, {Failed} failed" : text;
    }
}

/// <summary>The queue's rules, free of I/O so they are unit tested on their own.</summary>
public static class JobScheduler
{
    public const int MinConcurrency = 1;

    public const int MaxConcurrency = 5;

    public const int DefaultConcurrency = 2;

    public static IEnumerable<QueueJob> InOrder(IEnumerable<QueueJob> jobs) => jobs.OrderBy(j => j.Position).ThenBy(j => j.Id);

    /// <summary>Queued jobs to start now, first in order, so no more than <paramref name="concurrency"/> run at once.</summary>
    public static IReadOnlyList<QueueJob> NextToStart(IEnumerable<QueueJob> jobs, int concurrency)
    {
        var list = jobs.ToList();
        var free = Math.Clamp(concurrency, MinConcurrency, MaxConcurrency) - list.Count(j => j.IsActive);
        return free <= 0 ? [] : [.. InOrder(list).Where(j => j.State == JobState.Queued).Take(free)];
    }

    /// <summary>
    /// True when nothing is left to run: no job is running, queued or paused. Paused jobs count as
    /// unfinished, so pausing everything never triggers "sleep when finished".
    /// </summary>
    public static bool IsFinished(IEnumerable<QueueJob> jobs) =>
        jobs.All(j => !j.IsActive && j.State is not (JobState.Queued or JobState.Paused));

    /// <param name="batch">Jobs queued since the queue was last idle; taskbar progress averages over them.</param>
    public static QueueSummary Summarize(IEnumerable<QueueJob> jobs, IReadOnlySet<long> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var list = jobs.ToList();
        var inBatch = list.Where(j => batch.Contains(j.Id)).ToList();
        double? fraction = inBatch.Count == 0 || list.All(j => !j.IsActive && j.State != JobState.Queued)
            ? null
            : inBatch.Average(j => j.IsFinished ? 1 : j.Progress?.Fraction ?? j.Fraction ?? 0);

        return new QueueSummary(
            list.Count(j => j.IsActive),
            list.Count(j => j.State == JobState.Queued),
            list.Count(j => j.State == JobState.Paused),
            list.Count(j => j.State == JobState.Failed),
            list.Count(j => j.State == JobState.Done),
            fraction);
    }

    /// <summary>
    /// Moves a job <paramref name="offset"/> places (negative is up) and returns every job whose
    /// position changed. Positions are renumbered 0, 1, 2… so they stay unique.
    /// </summary>
    public static IReadOnlyList<QueueJob> Move(IEnumerable<QueueJob> jobs, long id, int offset)
    {
        var ordered = InOrder(jobs).ToList();
        var index = ordered.FindIndex(j => j.Id == id);
        if (index < 0)
        {
            return [];
        }

        var job = ordered[index];
        ordered.RemoveAt(index);
        ordered.Insert(Math.Clamp(index + offset, 0, ordered.Count), job);

        return [.. ordered.Select((j, i) => j.Position == i ? null : j with { Position = i }).OfType<QueueJob>()];
    }

    public static int NextPosition(IEnumerable<QueueJob> jobs) => jobs.Select(j => j.Position + 1).DefaultIfEmpty(0).Max();
}
