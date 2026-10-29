using Shouldly;
using SSnip.Core.Downloads;
using SSnip.Core.Queue;

namespace SSnip.Core.Tests.Queue;

public sealed class JobQueueTests : IAsyncDisposable
{
    private readonly InMemoryJobStore _store = new();
    private readonly ControlledExecutor _executor = new();
    private JobQueue _queue;

    public JobQueueTests() => _queue = new JobQueue(_store, _executor);

    public async ValueTask DisposeAsync() => await _queue.DisposeAsync();

    private static NewJob Job(string name, LiveOptions? live = null) =>
        new(new DownloadOptions { Url = name, OutputDirectory = @"E:\raw", Live = live });

    private async Task<IReadOnlyList<QueueJob>> StartWithAsync(params string[] names)
    {
        await _queue.LoadAsync();
        return await _queue.AddAsync(names.Select(n => Job(n)));
    }

    private QueueJob Get(string name) => _queue.Jobs.Single(j => j.Options.Url == name);

    private static async Task Eventually(Func<bool> condition, string because = "")
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new ShouldAssertException($"Condition not met in time. {because}");
            }

            await Task.Delay(10);
        }
    }

    private Task Running(params string[] names) =>
        Eventually(() => names.All(n => Get(n).IsActive) && _executor.Running == _queue.Jobs.Count(j => j.IsActive), $"waiting for {string.Join(", ", names)} to run");

    [Fact]
    public async Task Ten_jobs_run_two_at_a_time_in_queue_order()
    {
        var names = Enumerable.Range(1, 10).Select(i => $"job{i}").ToArray();
        await StartWithAsync(names);

        foreach (var pair in names.Chunk(2))
        {
            await Running(pair);
            _queue.Summary.Running.ShouldBe(2);
            foreach (var name in pair)
            {
                _executor.Finish(name);
            }
        }

        await Eventually(() => _queue.Jobs.All(j => j.State == JobState.Done));
        _executor.MaxRunning.ShouldBe(2);
        // Each pair starts together, so only the order between pairs is fixed; Running checks that.
        _executor.Started.Select(j => j.Options.Url).ShouldBe(names, ignoreOrder: true);
    }

    [Fact]
    public async Task Finished_jobs_keep_their_file_and_full_progress()
    {
        await StartWithAsync("a");
        await Running("a");
        _executor.Finish("a", JobOutcome.Done(@"E:\raw\a.mp4", "Already in the folder", "yt-dlp a"));

        await Eventually(() => Get("a").State == JobState.Done);
        var job = Get("a");
        job.FilePath.ShouldBe(@"E:\raw\a.mp4");
        job.Note.ShouldBe("Already in the folder");
        job.CommandLine.ShouldBe("yt-dlp a");
        job.Fraction.ShouldBe(1);
        job.Progress.ShouldBeNull();
        job.FinishedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Reported_title_and_progress_show_while_running()
    {
        await StartWithAsync("a");

        await Eventually(() => Get("a").Title == "Title of a");
        Get("a").Progress!.Stage.ShouldBe(DownloadStage.Video);
        Get("a").Progress!.Fraction.ShouldBe(0.5);
    }

    [Fact]
    public async Task Pausing_a_running_job_frees_its_slot_and_resume_continues_it()
    {
        await StartWithAsync("a", "b", "c");
        await Running("a", "b");

        _queue.Pause(Get("a").Id);

        await Running("b", "c");
        var paused = Get("a");
        paused.State.ShouldBe(JobState.Paused);
        paused.ResumePartial.ShouldBeTrue();
        paused.Fraction.ShouldBe(0.5);

        _queue.Resume(Get("a").Id);
        Get("a").State.ShouldBe(JobState.Queued);
        _executor.Finish("b");
        await Running("a", "c");
    }

    [Fact]
    public async Task Pausing_a_queued_job_skips_it_until_resumed()
    {
        await StartWithAsync("a", "b", "c");
        await Running("a", "b");

        _queue.Pause(Get("c").Id);
        _executor.Finish("a");

        await Eventually(() => Get("a").State == JobState.Done);
        Get("c").State.ShouldBe(JobState.Paused);
        _queue.Summary.Running.ShouldBe(1);
    }

    [Fact]
    public async Task Cancel_stops_running_and_queued_jobs()
    {
        await StartWithAsync("a", "b", "c");
        await Running("a", "b");

        _queue.Cancel(Get("c").Id);
        _queue.Cancel(Get("a").Id);

        await Eventually(() => Get("a").State == JobState.Cancelled);
        Get("c").State.ShouldBe(JobState.Cancelled);
        Get("a").FinishedAt.ShouldNotBeNull();
        _executor.Started.Select(j => j.Options.Url).ShouldNotContain("c");
    }

    [Fact]
    public async Task A_failed_job_never_blocks_the_rest()
    {
        await StartWithAsync("a", "b", "c");
        await Running("a", "b");

        _executor.Throw("a", new InvalidOperationException("boom"));
        _executor.Finish("b", JobOutcome.Failed("Private video", "yt-dlp b"));

        await Running("c");
        Get("a").State.ShouldBe(JobState.Failed);
        Get("a").Error.ShouldBe("boom");
        Get("b").Error.ShouldBe("Private video");
        Get("b").ResumePartial.ShouldBeTrue();
    }

    [Fact]
    public async Task Retry_runs_a_failed_job_again()
    {
        await StartWithAsync("a");
        _executor.Finish("a", JobOutcome.Failed("timeout"));
        await Eventually(() => Get("a").State == JobState.Failed);

        _queue.Retry(Get("a").Id);

        await Running("a");
        Get("a").Error.ShouldBeNull();
        _executor.Started.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Retry_of_a_done_job_starts_fresh()
    {
        await StartWithAsync("a");
        _executor.Finish("a");
        await Eventually(() => Get("a").State == JobState.Done);

        _queue.Retry(Get("a").Id);

        await Running("a");
        Get("a").FilePath.ShouldBeNull();
        Get("a").ResumePartial.ShouldBeFalse();
    }

    [Fact]
    public async Task Retry_ignores_running_and_queued_jobs()
    {
        await StartWithAsync("a", "b", "c");
        await Running("a", "b");

        _queue.Retry(Get("a").Id);
        _queue.Retry(Get("c").Id);

        _executor.Started.Count.ShouldBe(2);
        Get("c").State.ShouldBe(JobState.Queued);
    }

    [Fact]
    public async Task Moving_a_job_up_changes_what_runs_next()
    {
        _queue.SetConcurrency(1);
        await StartWithAsync("a", "b", "c", "d");
        await Running("a");

        _queue.Move(Get("d").Id, -2);
        _executor.Finish("a");

        await Running("d");
        _queue.Jobs.Select(j => j.Options.Url).ShouldBe(["a", "d", "b", "c"]);
    }

    [Fact]
    public async Task Raising_concurrency_starts_more_jobs()
    {
        await StartWithAsync("a", "b", "c", "d");
        await Running("a", "b");

        _queue.SetConcurrency(3);

        await Running("a", "b", "c");
        _queue.Concurrency.ShouldBe(3);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9, 5)]
    public void Concurrency_stays_between_one_and_five(int requested, int expected)
    {
        _queue.SetConcurrency(requested);
        _queue.Concurrency.ShouldBe(expected);
    }

    [Fact]
    public async Task Removing_a_running_job_stops_it_and_deletes_it()
    {
        await StartWithAsync("a", "b", "c");
        await Running("a", "b");
        var id = Get("a").Id;

        _queue.Remove(id);

        await Running("b", "c");
        _queue.Find(id).ShouldBeNull();
        await _queue.FlushAsync();
        _store.Stored.ShouldNotContain(j => j.Id == id);
    }

    [Fact]
    public async Task Clear_finished_keeps_queued_and_running_jobs()
    {
        await StartWithAsync("a", "b", "c");
        await Running("a", "b");
        _executor.Finish("a");
        _executor.Finish("b", JobOutcome.Failed("x"));
        await Running("c");

        _queue.ClearFinished().ShouldBe(2);

        _queue.Jobs.Select(j => j.Options.Url).ShouldBe(["c"]);
    }

    [Fact]
    public async Task The_queue_survives_closing_and_reopening()
    {
        await StartWithAsync("a", "b", "c", "d");
        await Running("a", "b");
        _executor.Finish("a");
        await Running("b", "c");

        await _queue.DisposeAsync();
        _store.Stored.Select(j => j.State).ShouldBe([JobState.Done, JobState.Queued, JobState.Queued, JobState.Queued]);
        _store.Stored[1].ResumePartial.ShouldBeTrue();

        _queue = new JobQueue(_store, _executor);
        await _queue.LoadAsync();

        await Running("b", "c");
        Get("a").State.ShouldBe(JobState.Done);
        Get("d").State.ShouldBe(JobState.Queued);
    }

    [Fact]
    public async Task Jobs_left_running_by_a_crash_resume_on_load()
    {
        await _store.AddAsync(new QueueJob { Title = "x", Options = Job("x").Options, State = JobState.Downloading });

        await _queue.LoadAsync();

        await Running("x");
        _executor.Started.Single().ResumePartial.ShouldBeTrue();
    }

    [Fact]
    public async Task Finished_fires_once_when_the_last_job_ends()
    {
        var finished = 0;
        _queue.Finished += (_, _) => Interlocked.Increment(ref finished);
        await StartWithAsync("a", "b", "c");
        await Running("a", "b");

        _executor.Finish("a");
        _executor.Finish("b", JobOutcome.Failed("x"));
        await Running("c");
        finished.ShouldBe(0);

        _executor.Finish("c");
        await Eventually(() => finished == 1);
        await Task.Delay(50);
        finished.ShouldBe(1);
        _queue.Summary.Fraction.ShouldBeNull();
    }

    [Fact]
    public async Task Finished_waits_while_jobs_are_paused()
    {
        var finished = 0;
        _queue.Finished += (_, _) => finished++;
        await StartWithAsync("a", "b");
        await Running("a", "b");

        _queue.Pause(Get("b").Id);
        _executor.Finish("a");

        await Eventually(() => Get("a").State == JobState.Done && Get("b").State == JobState.Paused);
        finished.ShouldBe(0);
    }

    [Fact]
    public async Task Stopping_a_live_recording_keeps_the_file()
    {
        await _queue.LoadAsync();
        await _queue.AddAsync([Job("live", new LiveOptions(FromStart: false))]);
        await Running("live");

        _queue.StopRecording(Get("live").Id);

        await Eventually(() => Get("live").State == JobState.Done);
        Get("live").Note.ShouldBe("Recording stopped");
    }

    [Fact]
    public async Task Stop_recording_ignores_normal_videos()
    {
        await StartWithAsync("a");
        await Running("a");

        _queue.StopRecording(Get("a").Id);
        await Task.Delay(50);

        Get("a").IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Taskbar_progress_averages_the_batch()
    {
        await StartWithAsync("a", "b", "c", "d");
        await Eventually(() => Get("a").Progress?.Fraction == 0.5 && Get("b").Progress?.Fraction == 0.5);

        // a and b at 50%, c and d not started.
        _queue.Summary.Fraction.ShouldBe(0.25);

        _executor.Finish("a");
        await Eventually(() => Get("c").Progress?.Fraction == 0.5);
        _queue.Summary.Fraction.ShouldBe((1 + 0.5 + 0.5 + 0) / 4);
    }

    [Fact]
    public async Task Every_change_reaches_the_store()
    {
        await StartWithAsync("a");
        _executor.Finish("a");
        await Eventually(() => Get("a").State == JobState.Done);
        await _queue.FlushAsync();

        var stored = _store.Stored.ShouldHaveSingleItem();
        stored.State.ShouldBe(JobState.Done);
        stored.Title.ShouldBe("Title of a");
        stored.Progress.ShouldBeNull();
    }

    [Fact]
    public async Task Every_change_has_a_higher_revision()
    {
        var revisions = new System.Collections.Concurrent.ConcurrentBag<long>();
        _queue.JobChanged += (_, job) => revisions.Add(job.Revision);
        await StartWithAsync("a");
        _executor.Finish("a");

        await Eventually(() => Get("a").State == JobState.Done);
        revisions.Count.ShouldBeGreaterThanOrEqualTo(3);
        revisions.Distinct().Count().ShouldBe(revisions.Count);
        Get("a").Revision.ShouldBe(revisions.Max());
    }

    [Fact]
    public async Task Pause_all_and_resume_all()
    {
        await StartWithAsync("a", "b", "c");
        await Running("a", "b");

        _queue.PauseAll();
        await Eventually(() => _queue.Jobs.All(j => j.State == JobState.Paused));

        _queue.ResumeAll();
        await Running("a", "b");
        Get("c").State.ShouldBe(JobState.Queued);
    }
}
