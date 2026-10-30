using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SSnip.Core.Downloads;
using SSnip.Core.Queue;
using SSnip.Infrastructure.Data;

namespace SSnip.Infrastructure.Queue;

/// <summary>Keeps the queue in SQLite. One short-lived context per call; <see cref="JobQueue"/> serialises the writes.</summary>
public sealed partial class SqliteJobStore(IDbContextFactory<AppDbContext> contexts, ILogger<SqliteJobStore> logger) : IJobStore
{
    public async Task<IReadOnlyList<QueueJob>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entities = await db.Jobs.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        var jobs = new List<QueueJob>(entities.Count);
        foreach (var entity in entities)
        {
            // A row this version cannot read is skipped and logged rather than breaking the queue.
            try
            {
                jobs.Add(ToJob(entity));
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                LogUnreadableJob(ex, entity.Id);
            }
        }

        return jobs;
    }

    public async Task<QueueJob> AddAsync(QueueJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = new JobEntity { Kind = "", Title = "", State = "", OptionsJson = "" };
        Copy(job, entity);
        db.Jobs.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return job with { Id = entity.Id };
    }

    public async Task UpdateAsync(QueueJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.Jobs.FindAsync([job.Id], cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            // Removed in the meantime.
            return;
        }

        Copy(job, entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Jobs.Where(j => j.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Copy(QueueJob job, JobEntity entity)
    {
        entity.Kind = job.Kind.ToString();
        entity.Title = job.Title;
        entity.State = job.State.ToString();
        entity.Position = job.Position;
        entity.OptionsJson = DownloadOptionsSerializer.Serialize(job.Options);
        entity.CommandLine = job.CommandLine;
        entity.FilePath = job.FilePath;
        entity.Error = job.Error;
        entity.Note = job.Note;
        entity.LogPath = job.LogPath;
        entity.Fraction = job.Progress?.Fraction ?? job.Fraction;
        entity.ResumePartial = job.ResumePartial;
        entity.CreatedAt = job.CreatedAt;
        entity.StartedAt = job.StartedAt;
        entity.FinishedAt = job.FinishedAt;
    }

    private static QueueJob ToJob(JobEntity entity) => new()
    {
        Id = entity.Id,
        Kind = Enum.Parse<JobKind>(entity.Kind),
        Title = entity.Title,
        State = Enum.Parse<JobState>(entity.State),
        Position = entity.Position,
        Options = DownloadOptionsSerializer.Deserialize(entity.OptionsJson),
        CommandLine = entity.CommandLine,
        FilePath = entity.FilePath,
        Error = entity.Error,
        Note = entity.Note,
        LogPath = entity.LogPath,
        Fraction = entity.Fraction,
        ResumePartial = entity.ResumePartial,
        CreatedAt = entity.CreatedAt,
        StartedAt = entity.StartedAt,
        FinishedAt = entity.FinishedAt,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping queue job {Id}: it could not be read")]
    private partial void LogUnreadableJob(Exception exception, long id);
}
