using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SSnip.Core.Downloads;
using SSnip.Core.Media;
using SSnip.Core.Processes;
using SSnip.Core.Tools;
using SSnip.Infrastructure.Downloads;

namespace SSnip.Integration.Tests;

/// <summary>Real network downloads. Uses only the owner's own Jemma Live clip.</summary>
[Trait("Category", "Integration")]
[Collection(ToolsCollection.Name)]
public sealed class YtDlpServiceTests(ToolsFixture tools) : IDisposable
{
    private readonly YtDlpService _service = new(tools.Runner, NullLogger<YtDlpService>.Instance);
    private readonly string _output = Path.Combine(tools.Paths.Cache, "it-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_output, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Fetches_info_with_h264_formats()
    {
        var info = await _service.FetchInfoAsync(SelfCheck.TestVideoUrl);

        info.Kind.ShouldBe(MediaKind.Video);
        info.Id.ShouldBe("BwiFiiPk_1s");
        info.Title.ShouldNotBeNullOrWhiteSpace();
        info.Channel.ShouldBe("Jemma Live");
        info.Duration.ShouldNotBeNull();
        info.Formats.ShouldContain(f => f.IsVideoOnly && f.VideoCodec!.StartsWith("avc1", StringComparison.Ordinal));
        info.Formats.ShouldContain(f => f.IsAudioOnly);
    }

    [Fact]
    public async Task Bad_link_throws_with_yt_dlp_error() =>
        (await Should.ThrowAsync<YtDlpException>(() => _service.FetchInfoAsync("https://www.youtube.com/watch?v=00000000000")))
            .Message.ShouldNotBeNullOrWhiteSpace();

    [Fact]
    public async Task Self_check_downloads_five_seconds()
    {
        var options = SelfCheck.TestDownload(tools.Paths) with { OutputDirectory = _output };

        var result = await _service.DownloadAsync(options);

        result.Succeeded.ShouldBeTrue(string.Join("; ", result.Errors));
        result.WasSkipped.ShouldBeFalse();
        File.Exists(result.FilePath).ShouldBeTrue();
        (await DurationAsync(result.FilePath!)).ShouldBe(5, tolerance: 0.5);
    }

    [Fact]
    public async Task Existing_file_is_reported_as_skipped_unless_overwrite_is_on()
    {
        // Whole files only: yt-dlp downloads --download-sections ranges again every time.
        var options = new DownloadOptions { Url = SelfCheck.TestVideoUrl, OutputDirectory = _output, Preset = FormatPreset.PreviewProxy };
        (await _service.DownloadAsync(options)).WasSkipped.ShouldBeFalse();

        var again = await _service.DownloadAsync(options);
        again.Succeeded.ShouldBeTrue();
        again.WasSkipped.ShouldBeTrue();

        var overwritten = await _service.DownloadAsync(options with { Overwrite = true });
        overwritten.WasSkipped.ShouldBeFalse();
    }

    [Fact]
    public async Task Editor_safe_downloads_h264_mp4_with_progress_and_a_hindi_filename()
    {
        var events = new ConcurrentQueue<YtDlpEvent>();
        var options = new DownloadOptions
        {
            Url = SelfCheck.TestVideoUrl,
            OutputDirectory = _output,
            FilenameTemplate = "मॉडर्न वॉरफेयर ｜ टेस्ट [%(id)s].%(ext)s",
            Preset = FormatPreset.EditorSafe,
        };

        var result = await _service.DownloadAsync(options, new SynchronousProgress<YtDlpEvent>(events.Enqueue));

        result.Succeeded.ShouldBeTrue(string.Join("; ", result.Errors));
        Path.GetFileName(result.FilePath).ShouldBe("मॉडर्न वॉरफेयर ｜ टेस्ट [BwiFiiPk_1s].mp4");
        File.Exists(result.FilePath).ShouldBeTrue();
        (await CodecAsync(result.FilePath!)).ShouldBe("h264");

        events.OfType<DownloadProgressEvent>().ShouldContain(p => !p.IsFinished && p.BytesPerSecond > 0);
        events.OfType<DownloadProgressEvent>().ShouldContain(p => p.IsFinished);
        events.OfType<PostProcessEvent>().ShouldContain(p => p.PostProcessor == "Merger");
        result.CommandLine.ShouldContain("vcodec:h264,res,fps,acodec:aac");
    }

    [Fact]
    public async Task Cancel_leaves_no_yt_dlp_or_ffmpeg_running()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new DownloadOptions
        {
            Url = SelfCheck.TestVideoUrl,
            OutputDirectory = _output,
            Preset = FormatPreset.MaxQuality,
            RateLimit = "100K",
            Overwrite = true,
        };

        using var cts = new CancellationTokenSource();
        var download = _service.DownloadAsync(options, new SynchronousProgress<YtDlpEvent>(e =>
        {
            if (e is DownloadProgressEvent { DownloadedBytes: > 0 })
            {
                started.TrySetResult();
            }
        }), cts.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(60));
        ToolProcesses().ShouldNotBeEmpty();

        await cts.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => download);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (ToolProcesses().Count > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
        }

        ToolProcesses().ShouldBeEmpty();
    }

    [Fact]
    public void Describe_command_matches_the_builder()
    {
        var options = new DownloadOptions { Url = SelfCheck.TestVideoUrl, OutputDirectory = @"E:\Footage\My Project\raw" };

        var command = _service.DescribeCommand(options);

        command.ShouldStartWith(CommandLineFormatter.Quote(tools.Paths.ToolPath(ExternalTool.YtDlp)) + " --ignore-config --encoding utf-8 --js-runtimes");
        command.ShouldContain(@"-P ""E:\Footage\My Project\raw""");
        command.ShouldEndWith("-- " + SelfCheck.TestVideoUrl);
    }

    /// <summary>yt-dlp and ffmpeg processes started from this test's bin folder.</summary>
    private List<Process> ToolProcesses() =>
    [
        .. Process.GetProcessesByName("yt-dlp").Concat(Process.GetProcessesByName("ffmpeg")).Where(p =>
        {
            try
            {
                return p.MainModule?.FileName?.StartsWith(tools.Paths.Bin, StringComparison.OrdinalIgnoreCase) ?? false;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }),
    ];

    private async Task<double> DurationAsync(string file)
    {
        var captured = await tools.Runner.RunAndCaptureAsync(new ProcessRequest(ExternalTool.Ffprobe, ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", file]));
        return double.Parse(captured.StandardOutput.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<string> CodecAsync(string file)
    {
        var captured = await tools.Runner.RunAndCaptureAsync(new ProcessRequest(ExternalTool.Ffprobe, ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_name", "-of", "csv=p=0", file]));
        return captured.StandardOutput.Trim();
    }
}
