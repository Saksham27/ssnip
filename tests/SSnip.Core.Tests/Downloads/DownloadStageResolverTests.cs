using Shouldly;
using SSnip.Core.Downloads;
using SSnip.Core.Media;

namespace SSnip.Core.Tests.Downloads;

public class DownloadStageResolverTests
{
    private static readonly MediaInfo Video = MediaInfoParser.Parse(Fixtures.Read("video.json"));

    [Theory]
    [InlineData(@"E:\raw\Title [TestVid0001].f137.mp4", DownloadStage.Video)]
    [InlineData(@"E:\raw\Title [TestVid0001].f137.mp4.part", DownloadStage.Video)]
    [InlineData(@"E:\raw\Title [TestVid0001].f140.m4a", DownloadStage.Audio)]
    [InlineData(@"E:\raw\Title [TestVid0001].fhls-720.mp4", DownloadStage.Video)]
    [InlineData(@"E:\raw\Title [TestVid0001].f18.mp4", DownloadStage.Downloading)]
    [InlineData(@"E:\raw\Title [TestVid0001].mp4", DownloadStage.Downloading)]
    public void Uses_the_format_suffix(string fileName, DownloadStage expected) =>
        DownloadStageResolver.FromFileName(fileName, Video).ShouldBe(expected);

    [Fact]
    public void Unknown_format_with_audio_extension_is_audio() =>
        DownloadStageResolver.FromFileName(@"E:\raw\x.f999.m4a", Video).ShouldBe(DownloadStage.Audio);

    [Fact]
    public void Works_without_info_or_name()
    {
        DownloadStageResolver.FromFileName(@"E:\raw\x.f137.mp4", null).ShouldBe(DownloadStage.Downloading);
        DownloadStageResolver.FromFileName(null, Video).ShouldBe(DownloadStage.Downloading);
    }

    [Theory]
    [InlineData("Merger", DownloadStage.Merging)]
    [InlineData("ExtractAudio", DownloadStage.ExtractingAudio)]
    [InlineData("VideoConvertor", DownloadStage.Converting)]
    [InlineData("VideoRemuxer", DownloadStage.Converting)]
    [InlineData("MoveFiles", DownloadStage.Finishing)]
    [InlineData("EmbedThumbnail", DownloadStage.PostProcessing)]
    public void Maps_post_processors(string name, DownloadStage expected) =>
        DownloadStageResolver.FromPostProcessor(name).ShouldBe(expected);

    [Fact]
    public void Every_stage_has_a_description() =>
        Enum.GetValues<DownloadStage>().ShouldAllBe(s => DownloadStageResolver.Describe(s).Length > 0);
}
