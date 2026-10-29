using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SSnip.Core;
using SSnip.Core.Downloads;
using SSnip.Core.Processes;
using SSnip.Core.Queue;
using SSnip.Core.Settings;
using SSnip.Core.Tools;
using SSnip.Infrastructure.Data;
using SSnip.Infrastructure.Downloads;
using SSnip.Infrastructure.Queue;
using SSnip.Infrastructure.Processes;
using SSnip.Infrastructure.Settings;
using SSnip.Infrastructure.Tools;

namespace SSnip.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSSnipInfrastructure(this IServiceCollection services, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        foreach (var directory in paths.Directories)
        {
            Directory.CreateDirectory(directory);
        }

        services.AddSingleton(paths);
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IYtDlpService, YtDlpService>();

        services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite($"Data Source={paths.DatabaseFile}"));
        services.AddSingleton<IJobStore, SqliteJobStore>();
        services.AddSingleton<IJobExecutor, YtDlpJobExecutor>();
        services.AddSingleton(sp => new JobQueue(sp.GetRequiredService<IJobStore>(), sp.GetRequiredService<IJobExecutor>()));
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IDependencyManager>(sp => new DependencyManager(
            paths,
            sp.GetRequiredService<IProcessRunner>(),
            CreateHttpClient(),
            sp.GetRequiredService<ILogger<DependencyManager>>()));
        return services;
    }

    /// <summary>Creates or upgrades the database, then loads and starts the stored queue.</summary>
    public static async Task StartQueueAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var contexts = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }

        await services.GetRequiredService<JobQueue>().LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SSnip", typeof(ServiceCollectionExtensions).Assembly.GetName().Version?.ToString(3) ?? "0"));
        return client;
    }
}
