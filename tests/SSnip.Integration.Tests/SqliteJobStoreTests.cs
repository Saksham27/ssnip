using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SSnip.Core.Downloads;
using SSnip.Core.Queue;
using SSnip.Infrastructure.Data;
using SSnip.Infrastructure.Queue;

namespace SSnip.Integration.Tests;

/// <summary>The real SQLite store on a temporary file. No network, so these run in CI.</summary>
public sealed class SqliteJobStoreTests : IAsyncLifetime
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"ssnip-test-{Guid.NewGuid():N}.db");
    private readonly Factory _contexts;
    private readonly SqliteJobStore _store;

    public SqliteJobStoreTests()
    {
        _contexts = new Factory(AppDbContext.CreateOptions(_file));
        _store = new SqliteJobStore(_contexts, NullLogger<SqliteJobStore>.Instance);
    }

    public async Task InitializeAsync()
    {
        await using var db = _contexts.CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_file);
        return Task.CompletedTask;
    }

    private static QueueJob NewJob(string title = "मॉडर्न वॉरफेयर ｜ Recap") => new()
    {
        Title = title,
        Position = 3,
        CreatedAt = new DateTimeOffset(2026, 10, 30, 9, 15, 0, TimeSpan.FromHours(5.5)),
        Options = new DownloadOptions
        {
            Url = "https://www.example.com/watch?v=TestVid0001",
            OutputDirectory = @"E:\Footage\रीकैप\raw",
            Preset = FormatPreset.AudioOnly,
            AudioFormat = AudioFormat.Wav,
            Subtitles = new SubtitleOptions(["en", "hi"]),
            SponsorBlock = SponsorBlockOptions.FootageDefault,
        },
    };

    [Fact]
    public async Task Added_jobs_get_ids_and_load_back_unchanged()
    {
        var added = await _store.AddAsync(NewJob());

        added.Id.ShouldBeGreaterThan(0);
        var loaded = (await _store.LoadAsync()).ShouldHaveSingleItem();
        loaded.Title.ShouldBe("मॉडर्न वॉरफेयर ｜ Recap");
        loaded.Position.ShouldBe(3);
        loaded.CreatedAt.ShouldBe(added.CreatedAt);
        loaded.State.ShouldBe(JobState.Queued);
        YtDlpArgsBuilder.Build((DownloadOptions)loaded.Options).ShouldBe(YtDlpArgsBuilder.Build((DownloadOptions)added.Options));
    }

    [Fact]
    public async Task Updates_store_state_and_last_progress_but_not_live_progress()
    {
        var job = await _store.AddAsync(NewJob());

        await _store.UpdateAsync(job with
        {
            State = JobState.Paused,
            Progress = new JobProgress(DownloadStage.Video, 0.42, 1000, 2000),
            Error = "x",
            Note = "y",
            FilePath = @"E:\raw\a.wav",
            CommandLine = "yt-dlp …",
            LogPath = @"C:\logs\job-1.log",
            ResumePartial = true,
            StartedAt = job.CreatedAt.AddMinutes(1),
        });

        var loaded = (await _store.LoadAsync()).Single();
        loaded.State.ShouldBe(JobState.Paused);
        loaded.Fraction.ShouldBe(0.42);
        loaded.Progress.ShouldBeNull();
        loaded.ResumePartial.ShouldBeTrue();
        loaded.FilePath.ShouldBe(@"E:\raw\a.wav");
        loaded.StartedAt.ShouldBe(job.CreatedAt.AddMinutes(1));
    }

    [Fact]
    public async Task Remove_deletes_only_that_job()
    {
        var first = await _store.AddAsync(NewJob("a"));
        await _store.AddAsync(NewJob("b"));

        await _store.RemoveAsync(first.Id);

        (await _store.LoadAsync()).Select(j => j.Title).ShouldBe(["b"]);
    }

    [Fact]
    public async Task Updating_a_removed_job_does_nothing()
    {
        var job = await _store.AddAsync(NewJob());
        await _store.RemoveAsync(job.Id);

        await _store.UpdateAsync(job with { State = JobState.Done });

        (await _store.LoadAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unreadable_row_is_skipped()
    {
        await _store.AddAsync(NewJob("good"));
        await using (var db = _contexts.CreateDbContext())
        {
            db.Jobs.Add(new JobEntity { Kind = "Download", Title = "bad", State = "NotAState", OptionsJson = "{}" });
            await db.SaveChangesAsync();
        }

        (await _store.LoadAsync()).Select(j => j.Title).ShouldBe(["good"]);
    }

    private sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
