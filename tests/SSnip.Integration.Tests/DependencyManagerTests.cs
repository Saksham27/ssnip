using Shouldly;
using SSnip.Core.Tools;

namespace SSnip.Integration.Tests;

[Trait("Category", "Integration")]
[Collection(ToolsCollection.Name)]
public class DependencyManagerTests(ToolsFixture tools)
{
    [Fact]
    public async Task Every_tool_is_installed_with_a_version()
    {
        var statuses = await tools.Dependencies.GetAllStatusAsync();

        statuses.Select(s => s.Tool).ShouldBe(Enum.GetValues<ExternalTool>(), ignoreOrder: true);
        foreach (var status in statuses)
        {
            status.IsHealthy.ShouldBeTrue($"{status.Tool}: {status.Error}");
            status.Path.ShouldStartWith(tools.Paths.Bin);
        }
    }

    [Fact]
    public async Task Missing_tool_reports_not_installed()
    {
        var empty = new SSnip.Core.AppPaths(Path.Combine(Path.GetTempPath(), "SSnip.Empty." + Guid.NewGuid().ToString("N")));
        var manager = new SSnip.Infrastructure.Tools.DependencyManager(
            empty,
            tools.Runner,
            new HttpClient(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SSnip.Infrastructure.Tools.DependencyManager>.Instance);

        var status = await manager.GetStatusAsync(ExternalTool.Deno);

        status.IsInstalled.ShouldBeFalse();
        status.IsHealthy.ShouldBeFalse();
    }

    [Fact]
    public async Task Yt_dlp_finds_deno_as_its_javascript_runtime()
    {
        var runtimes = await tools.Dependencies.GetJavaScriptRuntimesAsync();

        runtimes.ShouldContain(r => r.StartsWith("deno-", StringComparison.Ordinal));
    }
}
