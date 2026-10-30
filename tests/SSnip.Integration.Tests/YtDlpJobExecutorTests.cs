using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SSnip.Core.Downloads;
using SSnip.Core.Processes;
using SSnip.Core.Queue;
using SSnip.Core.Tools;
using SSnip.Infrastructure.Downloads;
using SSnip.Infrastructure.Queue;

namespace SSnip.Integration.Tests;

/// <summary>Queue jobs run by real yt-dlp. Uses only the owner's own Jemma Live clip.</summary>
[Trait("Category", "Integration")]
[Collection(ToolsCollection.Name)]
public sealed class YtDlpJobExecutorTests(ToolsFixture tools) : IDisposable
{
    private readonly YtDlpJobExecutor _executor = new(
        new YtDlpService(tools.Runner, NullLogger<YtDlpService>.Instance),
        tools.Runner,
        tools.Paths,
        NullLogger<YtDlpJobExecutor>.Instance);

    private readonly string _output = Path.Combine(tools.Paths.Cache, "it-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_output, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private QueueJob Job(DownloadOptions options) => new() { Id = Random.Shared.NextInt64(1, long.MaxValue), Title = options.Url, Options = options };

    [Fact]
    public async Task Runs_a_job_reports_title_and_stages_and_writes_its_log()
    {
        var updates = new ConcurrentQueue<JobUpdate>();
        var job = Job(new DownloadOptions
        {
            Url = SelfCheck.TestVideoUrl,
            OutputDirectory = _output,
            Preset = FormatPreset.Custom,
            Custom = new FormatSelection("160", "140"),
            InfoJsonDirectory = Path.Combine(_output, "info"),
            EmbedMetadata = true,
        });

        var outcome = await _executor.RunAsync(job, new SynchronousProgress<JobUpdate>(updates.Enqueue), CancellationToken.None, CancellationToken.None);

        outcome.State.ShouldBe(JobState.Done, outcome.Error);
        File.Exists(outcome.FilePath).ShouldBeTrue();
        updates.ShouldContain(u => u.Title == "Axe 5-Man Culling Blade Rampage | Dota 2 Highlights");
        updates.ShouldContain(u => u.Progress != null && u.Progress.Stage == DownloadStage.Video);
        updates.ShouldContain(u => u.Progress != null && u.Progress.Stage == DownloadStage.Audio);
        updates.ShouldContain(u => u.State == JobState.PostProcessing && u.Progress!.Stage == DownloadStage.Merging);
        File.Exists(Path.Combine(_output, "info", "youtube-BwiFiiPk_1s.info.json")).ShouldBeTrue();

        var log = await File.ReadAllTextAsync(tools.Paths.JobLogFile(job.Id));
        log.ShouldContain(outcome.CommandLine!);
        log.ShouldContain("FSFILE ");
        File.Delete(tools.Paths.JobLogFile(job.Id));
    }

    [Fact]
    public async Task Second_run_reports_already_downloaded()
    {
        var job = Job(new DownloadOptions { Url = SelfCheck.TestVideoUrl, OutputDirectory = _output, Preset = FormatPreset.PreviewProxy });
        var ignore = new SynchronousProgress<JobUpdate>(_ => { });

        (await _executor.RunAsync(job, ignore, CancellationToken.None, CancellationToken.None)).Note.ShouldBeNull();
        var again = await _executor.RunAsync(job, ignore, CancellationToken.None, CancellationToken.None);

        again.State.ShouldBe(JobState.Done);
        again.Note.ShouldBe(YtDlpJobExecutor.AlreadyDownloadedNote);
    }

    [Fact]
    public async Task A_date_filter_that_rejects_the_video_is_a_skip_not_a_failure()
    {
        var job = Job(new DownloadOptions { Url = SelfCheck.TestVideoUrl, OutputDirectory = _output, Preset = FormatPreset.PreviewProxy, DateAfter = new DateOnly(2099, 1, 1) });

        var outcome = await _executor.RunAsync(job, new SynchronousProgress<JobUpdate>(_ => { }), CancellationToken.None, CancellationToken.None);

        outcome.State.ShouldBe(JobState.Done, outcome.Error);
        outcome.Note.ShouldBe(YtDlpJobExecutor.OutsideDateRangeNote);
        outcome.FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task Audio_only_wav()
    {
        var job = Job(new DownloadOptions { Url = SelfCheck.TestVideoUrl, OutputDirectory = _output, Preset = FormatPreset.AudioOnly, AudioFormat = AudioFormat.Wav, EmbedThumbnail = true, EmbedMetadata = true });

        var outcome = await _executor.RunAsync(job, new SynchronousProgress<JobUpdate>(_ => { }), CancellationToken.None, CancellationToken.None);

        outcome.State.ShouldBe(JobState.Done, outcome.Error);
        Path.GetExtension(outcome.FilePath).ShouldBe(".wav");
        var codec = await tools.Runner.RunAndCaptureAsync(new ProcessRequest(ExternalTool.Ffprobe, ["-v", "error", "-show_entries", "stream=codec_name", "-of", "csv=p=0", outcome.FilePath!]));
        codec.StandardOutput.Trim().ShouldStartWith("pcm_");
    }
}
