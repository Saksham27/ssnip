using Microsoft.EntityFrameworkCore;
using SSnip.Core.Clipping;
using SSnip.Infrastructure.Data;

namespace SSnip.Infrastructure.Clipping;

public sealed class SqliteClipStore(IDbContextFactory<AppDbContext> contexts) : IClipStore
{
    public async Task<IReadOnlyList<ClipMark>> LoadAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.ClipDrafts.AsNoTracking().Where(c => c.Source == source).OrderBy(c => c.Position).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(r => new ClipMark { In = TimeSpan.FromMilliseconds(r.InMs), Out = TimeSpan.FromMilliseconds(r.OutMs), Name = r.Name, Tags = r.Tags, Note = r.Note })];
    }

    public async Task SaveAsync(string source, IReadOnlyList<ClipMark> clips, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(clips);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.ClipDrafts.Where(c => c.Source == source).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        db.ClipDrafts.AddRange(clips.Select((c, i) => new ClipDraftEntity
        {
            Source = source,
            Position = i,
            InMs = (long)c.In.TotalMilliseconds,
            OutMs = (long)c.Out.TotalMilliseconds,
            Name = c.Name,
            Tags = c.Tags,
            Note = c.Note,
        }));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
