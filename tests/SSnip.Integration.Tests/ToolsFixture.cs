using Microsoft.Extensions.Logging.Abstractions;
using SSnip.Core;
using SSnip.Core.Tools;
using SSnip.Infrastructure;
using SSnip.Infrastructure.Processes;
using SSnip.Infrastructure.Tools;

namespace SSnip.Integration.Tests;

/// <summary>
/// Real tools in a test-only app folder, downloaded through <see cref="DependencyManager"/> on first use
/// and kept between runs. Set SSNIP_TEST_ROOT to use another folder.
/// </summary>
public sealed class ToolsFixture : IAsyncLifetime
{
    public ToolsFixture()
    {
        var root = Environment.GetEnvironmentVariable("SSNIP_TEST_ROOT")
            ?? Path.Combine(Path.GetTempPath(), "SSnip.IntegrationTests");
        Paths = new AppPaths(root);
        foreach (var directory in Paths.Directories)
        {
            Directory.CreateDirectory(directory);
        }

        Runner = new ProcessRunner(Paths, NullLogger<ProcessRunner>.Instance);
        Dependencies = new DependencyManager(Paths, Runner, ServiceCollectionExtensions.CreateHttpClient(), NullLogger<DependencyManager>.Instance);
    }

    public AppPaths Paths { get; }

    public ProcessRunner Runner { get; }

    public DependencyManager Dependencies { get; }

    public async Task InitializeAsync()
    {
        foreach (var status in await Dependencies.GetAllStatusAsync())
        {
            if (!status.IsHealthy)
            {
                await Dependencies.InstallAsync(status.Tool);
            }
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition(Name)]
public sealed class ToolsCollection : ICollectionFixture<ToolsFixture>
{
    public const string Name = "Tools";
}
