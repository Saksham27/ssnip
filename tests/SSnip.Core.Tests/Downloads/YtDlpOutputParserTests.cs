using Shouldly;
using SSnip.Core.Downloads;
using SSnip.Core.Processes;

namespace SSnip.Core.Tests.Downloads;

public class YtDlpOutputParserTests
{
    // Shaped like real yt-dlp 2026.10 output; paths shortened.
    private const string Downloading = """FSPROG {"status": "downloading", "downloaded_bytes": 1024, "total_bytes": 12707607, "tmpfilename": "E:\\Footage\\Inbox\\raw\\Axe \uff5c Highlights [TestVid0001].f136.mp4.part", "filename": "E:\\Footage\\Inbox\\raw\\Axe \uff5c Highlights [TestVid0001].f136.mp4", "eta": 35, "speed": 360830.6558010586, "elapsed": 1.31, "ctx_id": null, "_eta_str": "00:35", "_percent_str": "  0.0%"}""";

    private const string Finished = """FSPROG {"downloaded_bytes": 860125, "total_bytes": 860125, "filename": "E:\\raw\\x [TestVid0001].f140.m4a", "status": "finished", "elapsed": 0.69, "ctx_id": null, "speed": 1239270.58}""";

    private const string Fragmented = """FSPROG {"status": "downloading", "downloaded_bytes": 5242880, "total_bytes_estimate": 104857600.4, "speed": null, "eta": null, "filename": "E:\\raw\\live.mp4", "fragment_index": 12, "fragment_count": 240}""";

    private static YtDlpEvent? Parse(string text, OutputStream stream = OutputStream.StandardOutput) =>
        YtDlpOutputParser.Parse(new OutputLine(stream, text));

    [Fact]
    public void Parses_a_downloading_line()
    {
        var p = Parse(Downloading).ShouldBeOfType<DownloadProgressEvent>();

        p.Status.ShouldBe("downloading");
        p.IsFinished.ShouldBeFalse();
        p.DownloadedBytes.ShouldBe(1024);
        p.TotalBytes.ShouldBe(12707607);
        p.IsTotalEstimate.ShouldBeFalse();
        p.BytesPerSecond!.Value.ShouldBe(360830.66, 0.01);
        p.Eta.ShouldBe(TimeSpan.FromSeconds(35));
        p.FileName.ShouldBe(@"E:\Footage\Inbox\raw\Axe ｜ Highlights [TestVid0001].f136.mp4");
        p.Fraction!.Value.ShouldBe(1024d / 12707607, 1e-9);
    }

    [Fact]
    public void Parses_a_finished_line()
    {
        var p = Parse(Finished).ShouldBeOfType<DownloadProgressEvent>();

        p.IsFinished.ShouldBeTrue();
        p.Fraction.ShouldBe(1);
        p.Eta.ShouldBeNull();
    }

    [Fact]
    public void Uses_the_estimate_and_fragments_when_total_is_unknown()
    {
        var p = Parse(Fragmented).ShouldBeOfType<DownloadProgressEvent>();

        p.TotalBytes.ShouldBe(104857600);
        p.IsTotalEstimate.ShouldBeTrue();
        p.BytesPerSecond.ShouldBeNull();
        p.Eta.ShouldBeNull();
        p.FragmentIndex.ShouldBe(12);
        p.FragmentCount.ShouldBe(240);
    }

    [Fact]
    public void Finished_with_elapsed_is_a_real_download()
    {
        var p = Parse(Finished).ShouldBeOfType<DownloadProgressEvent>();

        p.Elapsed!.Value.TotalSeconds.ShouldBe(0.69, 0.001);
        p.IsAlreadyDownloaded.ShouldBeFalse();
    }

    [Fact]
    public void Finished_without_elapsed_means_already_downloaded()
    {
        // What yt-dlp prints when the file exists and --no-overwrites skips it.
        const string skipped = """FSPROG {"filename": "E:\\raw\\x [TestVid0001].mp4", "status": "finished", "total_bytes": 3723757, "speed": null, "_speed_str": "Unknown B/s", "_elapsed_str": " Unknown", "_percent": 100.0}""";

        var p = Parse(skipped).ShouldBeOfType<DownloadProgressEvent>();

        p.IsAlreadyDownloaded.ShouldBeTrue();
        p.DownloadedBytes.ShouldBe(0);
    }

    [Fact]
    public void Downloading_is_never_already_downloaded() =>
        Parse(Downloading).ShouldBeOfType<DownloadProgressEvent>().IsAlreadyDownloaded.ShouldBeFalse();

    [Fact]
    public void Fraction_is_null_without_a_total() =>
        Parse("""FSPROG {"status": "downloading", "downloaded_bytes": 10}""").ShouldBeOfType<DownloadProgressEvent>().Fraction.ShouldBeNull();

    [Fact]
    public void Fraction_never_exceeds_one() =>
        Parse("""FSPROG {"status": "downloading", "downloaded_bytes": 150, "total_bytes_estimate": 100}""").ShouldBeOfType<DownloadProgressEvent>().Fraction.ShouldBe(1);

    [Theory]
    [InlineData("""FSPP {"status": "started", "postprocessor": "Merger", "_default_template": "Merger started"}""", "Merger", true)]
    [InlineData("""FSPP {"status": "finished", "postprocessor": "MoveFiles"}""", "MoveFiles", false)]
    public void Parses_post_processing(string line, string name, bool started) =>
        Parse(line).ShouldBe(new PostProcessEvent(name, started));

    [Fact]
    public void Parses_the_final_path_with_unicode() =>
        Parse(@"FSFILE E:\Footage\Inbox\raw\जब रोटी सब्जी ｜ Breakfast [TestVid0001].mp4")
            .ShouldBe(new FinalFileEvent(@"E:\Footage\Inbox\raw\जब रोटी सब्जी ｜ Breakfast [TestVid0001].mp4"));

    [Fact]
    public void Parses_errors_and_warnings()
    {
        Parse("ERROR: [youtube] TestVid0001: Private video. Sign in if you've been granted access", OutputStream.StandardError)
            .ShouldBe(new ErrorEvent("[youtube] TestVid0001: Private video. Sign in if you've been granted access"));
        Parse("WARNING: [youtube] Falling back to generic n function search", OutputStream.StandardError)
            .ShouldBe(new WarningEvent("[youtube] Falling back to generic n function search"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[download] Destination: x.mp4")]
    [InlineData("FSPROG not json")]
    [InlineData("FSPROG [1,2]")]
    [InlineData("FSPP {}")]
    [InlineData("FSFILE   ")]
    public void Ignores_other_lines(string line) => Parse(line).ShouldBeNull();
}
