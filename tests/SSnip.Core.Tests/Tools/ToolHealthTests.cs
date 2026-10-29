using Shouldly;
using SSnip.Core.Tools;

namespace SSnip.Core.Tests.Tools;

public class ToolHealthTests
{
    private static ToolStatus Healthy(ExternalTool tool) => new(tool, "x.exe", true, "1.0", null);

    private static ToolStatus Missing(ExternalTool tool) => new(tool, "x.exe", false, null, null);

    [Fact]
    public void All_healthy_reports_ok() =>
        ToolHealth.Summarise([Healthy(ExternalTool.YtDlp), Healthy(ExternalTool.Deno)]).ShouldBe((true, "Tools OK"));

    [Fact]
    public void One_missing_names_it() =>
        ToolHealth.Summarise([Healthy(ExternalTool.YtDlp), Missing(ExternalTool.Deno)]).ShouldBe((false, "Deno missing"));

    [Fact]
    public void Several_missing_counts_them() =>
        ToolHealth.Summarise([Missing(ExternalTool.Ffmpeg), Missing(ExternalTool.Ffprobe)]).ShouldBe((false, "2 tools missing"));

    [Fact]
    public void Installed_without_a_version_is_not_healthy() =>
        new ToolStatus(ExternalTool.YtDlp, "x.exe", true, null, "Could not read version").IsHealthy.ShouldBeFalse();
}
