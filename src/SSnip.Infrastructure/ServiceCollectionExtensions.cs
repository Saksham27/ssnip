using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SSnip.Core;
using SSnip.Core.Processes;
using SSnip.Core.Settings;
using SSnip.Core.Tools;
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
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IDependencyManager>(sp => new DependencyManager(
            paths,
            sp.GetRequiredService<IProcessRunner>(),
            CreateHttpClient(),
            sp.GetRequiredService<ILogger<DependencyManager>>()));
        return services;
    }

    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SSnip", typeof(ServiceCollectionExtensions).Assembly.GetName().Version?.ToString(3) ?? "0"));
        return client;
    }
}
