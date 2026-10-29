using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Shouldly;
using SSnip.Core.Processes;
using SSnip.Core.Tools;

namespace SSnip.Integration.Tests;

[Trait("Category", "Integration")]
[Collection(ToolsCollection.Name)]
public class ProcessRunnerTests(ToolsFixture tools)
{
    [Fact]
    public async Task Runs_yt_dlp_version_and_streams_output()
    {
        var lines = new ConcurrentQueue<OutputLine>();

        var result = await tools.Runner.RunAsync(
            new ProcessRequest(ExternalTool.YtDlp, ["--version"]),
            new SynchronousProgress<OutputLine>(lines.Enqueue));

        result.ExitCode.ShouldBe(0);
        result.CommandLine.ShouldContain("--ignore-config");
        result.CommandLine.ShouldEndWith("--version");
        var stdout = lines.Where(l => l.Stream == OutputStream.StandardOutput).Select(l => l.Text).ToList();
        stdout.ShouldHaveSingleItem();
        ToolVersionParser.Parse(ExternalTool.YtDlp, stdout[0]).ShouldNotBeNull();
    }

    [Fact]
    public async Task Arguments_reach_the_tool_unchanged()
    {
        string[] arguments = ["My Clip [final].mp4", "say \"hi\"", @"D:\Footage\COD Recap\", "मॉडर्न वॉरफेयर रीकैप", ""];
        var lines = new ConcurrentQueue<OutputLine>();

        await tools.Runner.RunAsync(
            new ProcessRequest(ExternalTool.Deno, ["eval", "console.log(JSON.stringify(Deno.args))", .. arguments]),
            new SynchronousProgress<OutputLine>(lines.Enqueue));

        var json = lines.Single(l => l.Stream == OutputStream.StandardOutput).Text;
        JsonSerializer.Deserialize<string[]>(json).ShouldBe(arguments);
    }

    [Fact]
    public async Task Cancel_kills_the_whole_process_tree()
    {
        // Deno starts ffmpeg as a child that would run forever, the same shape as yt-dlp merging with ffmpeg.
        const string script = """
            const child = new Deno.Command("ffmpeg", {
              args: ["-hide_banner", "-loglevel", "error", "-re", "-f", "lavfi", "-i", "anullsrc", "-f", "null", "-"],
              stdout: "null", stderr: "null",
            }).spawn();
            console.log("CHILD " + child.pid);
            await child.status;
            """;

        var childPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new SynchronousProgress<OutputLine>(line =>
        {
            if (line.Text.StartsWith("CHILD ", StringComparison.Ordinal))
            {
                childPid.TrySetResult(int.Parse(line.Text[6..], System.Globalization.CultureInfo.InvariantCulture));
            }
        });

        using var cts = new CancellationTokenSource();
        var run = tools.Runner.RunAsync(new ProcessRequest(ExternalTool.Deno, ["eval", script]), output, cts.Token);

        var pid = await childPid.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var ffmpeg = Process.GetProcessById(pid);
        ffmpeg.ProcessName.ShouldBe("ffmpeg");

        await cts.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => run);

        ffmpeg.WaitForExit(TimeSpan.FromSeconds(10)).ShouldBeTrue("ffmpeg child should be killed with its parent");
    }

    [Fact]
    public async Task Missing_tool_throws_file_not_found()
    {
        var empty = new SSnip.Core.AppPaths(Path.Combine(Path.GetTempPath(), "SSnip.Empty." + Guid.NewGuid().ToString("N")));
        var runner = new SSnip.Infrastructure.Processes.ProcessRunner(empty, Microsoft.Extensions.Logging.Abstractions.NullLogger<SSnip.Infrastructure.Processes.ProcessRunner>.Instance);

        await Should.ThrowAsync<FileNotFoundException>(() => runner.RunAsync(new ProcessRequest(ExternalTool.YtDlp, ["--version"])));
    }
}
