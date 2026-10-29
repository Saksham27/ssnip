using Shouldly;
using SSnip.Core.Tools;

namespace SSnip.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void Lays_out_folders_under_the_root()
    {
        var paths = new AppPaths(@"C:\Data\SSnip");

        paths.Bin.ShouldBe(@"C:\Data\SSnip\bin");
        paths.Data.ShouldBe(@"C:\Data\SSnip\data");
        paths.Logs.ShouldBe(@"C:\Data\SSnip\logs");
        paths.Cache.ShouldBe(@"C:\Data\SSnip\cache");
        paths.SettingsFile.ShouldBe(@"C:\Data\SSnip\settings.json");
        paths.DatabaseFile.ShouldBe(@"C:\Data\SSnip\data\footage.db");
    }

    [Fact]
    public void Default_root_is_local_app_data() =>
        AppPaths.Default.Root.ShouldBe(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SSnip"));

    [Fact]
    public void Every_tool_has_a_path() =>
        Enum.GetValues<ExternalTool>().Select(new AppPaths(@"C:\x").ToolPath).Distinct().Count().ShouldBe(Enum.GetValues<ExternalTool>().Length);
}
