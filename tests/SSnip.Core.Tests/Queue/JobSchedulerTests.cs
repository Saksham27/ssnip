using Shouldly;
using SSnip.Core.Downloads;
using SSnip.Core.Queue;

namespace SSnip.Core.Tests.Queue;

public class JobSchedulerTests
{
    private static QueueJob Job(long id, JobState state = JobState.Queued, int? position = null, double? fraction = null) => new()
    {
        Id = id,
        Title = $"job{id}",
        State = state,
        Position = position ?? (int)id,
        Fraction = fraction,
        Options = new DownloadOptions { Url = $"https://www.example.com/{id}", OutputDirectory = @"E:\raw" },
    };

    [Fact]
    public void Starts_queued_jobs_in_order_up_to_the_limit() =>
        JobScheduler.NextToStart([Job(3), Job(1), Job(2), Job(4)], 2).Select(j => j.Id).ShouldBe([1, 2]);

    [Fact]
    public void Running_jobs_use_up_slots() =>
        JobScheduler.NextToStart([Job(1, JobState.Downloading), Job(2, JobState.PostProcessing), Job(3)], 2).ShouldBeEmpty();

    [Fact]
    public void Paused_failed_and_done_jobs_are_never_started() =>
        JobScheduler.NextToStart([Job(1, JobState.Paused), Job(2, JobState.Failed), Job(3, JobState.Done), Job(4)], 5).Select(j => j.Id).ShouldBe([4]);

    [Fact]
    public void Position_wins_over_id() =>
        JobScheduler.NextToStart([Job(1, position: 9), Job(2, position: 0)], 1).Single().Id.ShouldBe(2);

    [Theory]
    [InlineData(JobState.Done, true)]
    [InlineData(JobState.Failed, true)]
    [InlineData(JobState.Cancelled, true)]
    [InlineData(JobState.Queued, false)]
    [InlineData(JobState.Paused, false)]
    [InlineData(JobState.Downloading, false)]
    public void Finished_only_when_nothing_is_left(JobState other, bool finished) =>
        JobScheduler.IsFinished([Job(1, JobState.Done), Job(2, other)]).ShouldBe(finished);

    [Fact]
    public void Moving_up_renumbers_positions()
    {
        var moved = JobScheduler.Move([Job(1, position: 10), Job(2, position: 20), Job(3, position: 30)], 3, -1);

        moved.OrderBy(j => j.Id).Select(j => (j.Id, j.Position)).ShouldBe([(1L, 0), (2L, 2), (3L, 1)]);
    }

    [Fact]
    public void Moving_past_the_ends_stops_at_the_ends()
    {
        var jobs = new[] { Job(1, position: 0), Job(2, position: 1), Job(3, position: 2) };

        JobScheduler.Move(jobs, 1, -5).ShouldBeEmpty();
        JobScheduler.Move(jobs, 1, 5).Single(j => j.Id == 1).Position.ShouldBe(2);
    }

    [Fact]
    public void Moving_an_unknown_job_changes_nothing() =>
        JobScheduler.Move([Job(1)], 99, 1).ShouldBeEmpty();

    [Fact]
    public void Next_position_follows_the_last() =>
        JobScheduler.NextPosition([Job(1, position: 4), Job(2, position: 7)]).ShouldBe(8);

    [Fact]
    public void Summary_counts_and_text()
    {
        var summary = JobScheduler.Summarize([Job(1, JobState.Downloading), Job(2, JobState.Downloading), Job(3), Job(4), Job(5), Job(6, JobState.Paused), Job(7, JobState.Failed)], new HashSet<long>());

        summary.ToString().ShouldBe("2 running, 3 queued, 1 paused, 1 failed");
        summary.IsIdle.ShouldBeFalse();
    }

    [Fact]
    public void Summary_text_matches_the_status_bar_when_quiet() =>
        JobScheduler.Summarize([], new HashSet<long>()).ToString().ShouldBe("0 running, 0 queued");

    [Fact]
    public void Batch_progress_counts_finished_jobs_as_whole()
    {
        var summary = JobScheduler.Summarize([Job(1, JobState.Done), Job(2, JobState.Downloading, fraction: 0.5), Job(3)], new HashSet<long> { 1, 2, 3 });

        summary.Fraction.ShouldBe(0.5);
    }

    [Fact]
    public void No_progress_when_idle() =>
        JobScheduler.Summarize([Job(1, JobState.Done)], new HashSet<long> { 1 }).Fraction.ShouldBeNull();
}
