using Shouldly;
using SSnip.Core.Clipping;

namespace SSnip.Core.Tests.Clipping;

public class FfmpegProgressParserTests
{
    // Shaped like real ffmpeg 8 -progress pipe:1 output.
    private const string Block = """
        frame=250
        fps=124.6
        bitrate=12345.6kbits/s
        total_size=15433728
        out_time_us=10000000
        out_time_ms=10000000
        out_time=00:00:10.000000
        dup_frames=0
        drop_frames=0
        speed=4.98x
        progress=continue
        """;

    private static List<FfmpegProgress> ReadAll(FfmpegProgressParser parser, string text) =>
        [.. text.Split('\n').Select(l => parser.Read(l.TrimEnd('\r'))).OfType<FfmpegProgress>()];

    [Fact]
    public void A_block_gives_position_and_speed()
    {
        var progress = ReadAll(new FfmpegProgressParser(), Block).ShouldHaveSingleItem();

        progress.Position.ShouldBe(TimeSpan.FromSeconds(10));
        progress.Speed.ShouldBe(4.98);
        progress.IsEnd.ShouldBeFalse();
        progress.FractionOf(TimeSpan.FromSeconds(40)).ShouldBe(0.25);
        progress.EtaFor(TimeSpan.FromSeconds(40))!.Value.TotalSeconds.ShouldBe(30 / 4.98, 0.001);
    }

    [Fact]
    public void End_is_complete()
    {
        var parser = new FfmpegProgressParser();
        ReadAll(parser, Block);

        var end = parser.Read("progress=end")!;

        end.IsEnd.ShouldBeTrue();
        end.FractionOf(null).ShouldBe(1);
    }

    [Fact]
    public void Unknown_speed_and_duration()
    {
        var progress = ReadAll(new FfmpegProgressParser(), "out_time_us=N/A\nspeed=N/A\nprogress=continue").Single();

        progress.Speed.ShouldBeNull();
        progress.FractionOf(null).ShouldBeNull();
        progress.EtaFor(TimeSpan.FromSeconds(10)).ShouldBeNull();
    }

    [Fact]
    public void Fraction_never_exceeds_one() =>
        ReadAll(new FfmpegProgressParser(), "out_time_us=50000000\nprogress=continue").Single().FractionOf(TimeSpan.FromSeconds(40)).ShouldBe(1);

    [Fact]
    public void Lists_encoders_from_ffmpeg()
    {
        const string output = """
            Encoders:
             V..... = Video
             A..... = Audio
             ------
             V....D h264_amf             AMD AMF H.264 Encoder (codec h264)
             V..... h264_qsv             H.264 / AVC (Intel Quick Sync Video acceleration) (codec h264)
             VFS..D dnxhd                VC3/DNxHD
             A....D aac                  AAC (Advanced Audio Coding)
            """;

        EncoderList.Parse(output).ShouldBe(["h264_amf", "h264_qsv", "dnxhd", "aac"], ignoreOrder: true);
    }
}
