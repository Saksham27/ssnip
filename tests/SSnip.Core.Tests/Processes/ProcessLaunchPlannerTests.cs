using Shouldly;
using SSnip.Core.Processes;
using SSnip.Core.Tools;

namespace SSnip.Core.Tests.Processes;

public class ProcessLaunchPlannerTests
{
    private static readonly AppPaths Paths = new(@"C:\Data\SSnip");

    [Fact]
    public void YtDlp_gets_ignore_config_and_our_deno_first()
    {
        var plan = ProcessLaunchPlanner.Plan(new ProcessRequest(ExternalTool.YtDlp, ["--version"]), Paths, null);

        plan.Arguments.ShouldBe(["--ignore-config", "--js-runtimes", @"deno:C:\Data\SSnip\bin\deno.exe", "--version"]);
    }

    [Fact]
    public void YtDlp_flags_the_caller_already_set_are_not_added_twice()
    {
        var plan = ProcessLaunchPlanner.Plan(new ProcessRequest(ExternalTool.YtDlp, ["--ignore-config", "--js-runtimes", "node", "-J"]), Paths, null);

        plan.Arguments.ShouldBe(["--ignore-config", "--js-runtimes", "node", "-J"]);
    }

    [Theory]
    [InlineData(ExternalTool.Ffmpeg)]
    [InlineData(ExternalTool.Ffprobe)]
    [InlineData(ExternalTool.Deno)]
    public void Other_tools_keep_their_arguments_unchanged(ExternalTool tool)
    {
        var plan = ProcessLaunchPlanner.Plan(new ProcessRequest(tool, ["-version"]), Paths, null);

        plan.Arguments.ShouldBe(["-version"]);
    }

    [Theory]
    [InlineData(ExternalTool.YtDlp, @"C:\Data\SSnip\bin\yt-dlp.exe")]
    [InlineData(ExternalTool.Ffmpeg, @"C:\Data\SSnip\bin\ffmpeg.exe")]
    [InlineData(ExternalTool.Ffprobe, @"C:\Data\SSnip\bin\ffprobe.exe")]
    [InlineData(ExternalTool.Deno, @"C:\Data\SSnip\bin\deno.exe")]
    public void Executable_resolves_into_the_bin_folder(ExternalTool tool, string expected) =>
        ProcessLaunchPlanner.Plan(new ProcessRequest(tool, []), Paths, null).ExecutablePath.ShouldBe(expected);

    [Fact]
    public void Bin_folder_is_prepended_to_path()
    {
        var plan = ProcessLaunchPlanner.Plan(new ProcessRequest(ExternalTool.YtDlp, []), Paths, @"C:\Windows;C:\Tools");

        plan.Environment["PATH"].ShouldBe(@"C:\Data\SSnip\bin;C:\Windows;C:\Tools");
    }

    [Fact]
    public void Bin_folder_is_the_whole_path_when_none_is_inherited() =>
        ProcessLaunchPlanner.Plan(new ProcessRequest(ExternalTool.YtDlp, []), Paths, null).Environment["PATH"].ShouldBe(@"C:\Data\SSnip\bin");

    [Fact]
    public void Python_output_is_utf8() =>
        ProcessLaunchPlanner.Plan(new ProcessRequest(ExternalTool.YtDlp, []), Paths, null).Environment["PYTHONIOENCODING"].ShouldBe("utf-8");

    [Fact]
    public void Arguments_with_spaces_quotes_and_hindi_stay_separate()
    {
        string[] arguments = ["-o", @"D:\Footage\COD Recap\raw\%(title)s.%(ext)s", "--match-filter", "title ~= 'Soap \"Final\"'", "मॉडर्न वॉरफेयर"];

        var plan = ProcessLaunchPlanner.Plan(new ProcessRequest(ExternalTool.YtDlp, arguments), Paths, null);

        plan.Arguments.Skip(3).ShouldBe(arguments);
    }

    [Fact]
    public void Command_line_shows_the_final_arguments()
    {
        var plan = ProcessLaunchPlanner.Plan(new ProcessRequest(ExternalTool.YtDlp, ["-o", "My Clip.mp4"]), Paths, null);

        plan.CommandLine.ShouldBe(@"C:\Data\SSnip\bin\yt-dlp.exe --ignore-config --js-runtimes deno:C:\Data\SSnip\bin\deno.exe -o ""My Clip.mp4""");
    }

    [Fact]
    public void Request_arguments_are_copied_not_shared()
    {
        var arguments = new List<string> { "--version" };

        var plan = ProcessLaunchPlanner.Plan(new ProcessRequest(ExternalTool.Ffmpeg, arguments), Paths, null);
        arguments.Add("-y");

        plan.Arguments.ShouldBe(["--version"]);
    }
}
