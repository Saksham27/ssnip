using Shouldly;
using SSnip.Core.Clipping;
using SSnip.Core.Downloads;

namespace SSnip.Core.Tests.Clipping;

public class ClipJobBuilderTests
{
    private const string Url = "https://www.example.com/watch?v=TestVid0001";

    private static readonly ClipBatch Batch = new(@"E:\clips", TimeSpan.FromSeconds(2), CutMode.Accurate, new EditorProfile(EditorProfileKind.H264, "h264_amf"), InfoJsonDirectory: @"C:\data\info");

    private static ClipMark Clip(double a, double b, string name = "") =>
        new() { In = TimeSpan.FromSeconds(a), Out = TimeSpan.FromSeconds(b), Name = name };

    [Fact]
    public void Url_clips_become_section_downloads_with_handles()
    {
        var job = ClipJobBuilder.ForUrl(Url, "Recap", [Clip(30, 40, "Soap scene")], TimeSpan.FromMinutes(5), Batch).Single();
        var options = job.Options.ShouldBeOfType<DownloadOptions>();

        options.Sections.ShouldBe(["*0:00:28.000-0:00:42.000"]);
        options.CutMode.ShouldBe(CutMode.Accurate);
        options.FilenameTemplate.ShouldBe("Soap scene [%(id)s].%(ext)s");
        options.Profile!.Encoder.ShouldBe("h264_amf");
        options.Overwrite.ShouldBeTrue();
        options.InfoJsonDirectory.ShouldBe(@"C:\data\info");
        job.Title.ShouldBe("Soap scene · Recap");
    }

    [Fact]
    public void Keep_original_means_no_conversion() =>
        ClipJobBuilder.ForUrl(Url, null, [Clip(1, 2)], null, Batch with { Profile = EditorProfile.Original })
            .Single().Options.ShouldBeOfType<DownloadOptions>().Profile.ShouldBeNull();

    [Fact]
    public void Zero_handles_keep_the_exact_marks() =>
        ClipJobBuilder.ForUrl(Url, null, [Clip(10.25, 20.5)], null, Batch with { Handles = TimeSpan.Zero })
            .Single().Options.ShouldBeOfType<DownloadOptions>().Sections.ShouldBe(["*0:00:10.250-0:00:20.500"]);

    [Fact]
    public void Names_are_made_unique_and_empty_ones_numbered()
    {
        var jobs = ClipJobBuilder.ForUrl(Url, null, [Clip(1, 2, "Boss"), Clip(3, 4), Clip(5, 6, "boss"), Clip(7, 8, "Boss")], null, Batch);

        jobs.Select(j => j.Title).ShouldBe(["Boss", "Clip 2", "boss (2)", "Boss (3)"]);
    }

    [Fact]
    public void Invalid_clips_are_skipped() =>
        ClipJobBuilder.ForUrl(Url, null, [Clip(5, 5), Clip(1, 2, "ok")], null, Batch).Single().Title.ShouldBe("ok");

    [Fact]
    public void Local_clips_become_ffmpeg_cuts()
    {
        var job = ClipJobBuilder.ForLocalFile(@"E:\raw\match.mkv", [Clip(1, 59, "Ace")], TimeSpan.FromMinutes(1), Batch).Single();
        var cut = job.Options.ShouldBeOfType<LocalCutOptions>();

        cut.Start.ShouldBe(TimeSpan.Zero);
        cut.End.ShouldBe(TimeSpan.FromMinutes(1));
        cut.Mode.ShouldBe(CutMode.Accurate);
        cut.OutputPath.ShouldBe(@"E:\clips\Ace.mp4");
        job.Title.ShouldBe("Ace · match.mkv");
    }
}
