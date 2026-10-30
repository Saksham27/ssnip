using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SSnip.Infrastructure.Data;

/// <summary>
/// SSnip's SQLite database (<c>data\footage.db</c>). Phase 2 stores the queue; projects, sources,
/// assets and clips join it in Phase 4. Schema changes go through EF Core migrations:
/// <c>dotnet ef migrations add &lt;Name&gt; -p src/SSnip.Infrastructure -s src/SSnip.Infrastructure -o Data/Migrations</c>.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<JobEntity> Jobs => Set<JobEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<JobEntity>(job =>
        {
            job.ToTable("Jobs");
            job.HasKey(j => j.Id);
            job.Property(j => j.Kind).HasMaxLength(32);
            job.Property(j => j.State).HasMaxLength(32);
            job.HasIndex(j => j.State);
        });
    }

    public static DbContextOptions<AppDbContext> CreateOptions(string databaseFile) =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={databaseFile}").Options;
}

/// <summary>A stored queue job. State, kind and options are kept as text so the file stays readable.</summary>
public sealed class JobEntity
{
    public long Id { get; set; }

    public required string Kind { get; set; }

    public required string Title { get; set; }

    public required string State { get; set; }

    public int Position { get; set; }

    /// <summary>The job's <c>DownloadOptions</c> as JSON.</summary>
    public required string OptionsJson { get; set; }

    public string? CommandLine { get; set; }

    public string? FilePath { get; set; }

    public string? Error { get; set; }

    public string? Note { get; set; }

    public string? LogPath { get; set; }

    public double? Fraction { get; set; }

    public bool ResumePartial { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}

/// <summary>Lets <c>dotnet ef</c> build the model without starting the app.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(AppDbContext.CreateOptions(Path.Combine(Path.GetTempPath(), "ssnip-design.db")));
}
