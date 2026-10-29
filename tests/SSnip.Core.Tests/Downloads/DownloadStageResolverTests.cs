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

    [Theory]
    [InlineData("avc1.640028", null, DownloadStage.Video)]
    [InlineData(null, "opus", DownloadStage.Audio)]
    [InlineData("avc1.42001E", "mp4a.40.2", DownloadStage.Downloading)]
    public void Codecs_decide_without_media_info(string? video, string? audio, DownloadStage expected) =>
        DownloadStageResolver.FromProgress(Progress(@"E:\raw\Title [TestVid0001].f251.webm", video, audio)).ShouldBe(expected);

    [Fact]
    public void Falls_back_to_the_file_name_without_codecs() =>
        DownloadStageResolver.FromProgress(Progress(@"E:\raw\Title [TestVid0001].f140.m4a", null, null)).ShouldBe(DownloadStage.Audio);

    private static DownloadProgressEvent Progress(string fileName, string? video, string? audio) =>
        new("downloading", 1, 10, false, null, null, fileName, null, null, null, video, audio);

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
