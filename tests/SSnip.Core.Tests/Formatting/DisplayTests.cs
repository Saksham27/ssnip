using Shouldly;
using SSnip.Core.Formatting;

namespace SSnip.Core.Tests.Formatting;

public class DisplayTests
{
    [Theory]
    [InlineData(null, "–")]
    [InlineData(-1L, "–")]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(12707607L, "12.1 MB")]
    [InlineData(950000000L, "906.0 MB")]
    [InlineData(5368709120L, "5.0 GB")]
    public void Bytes(long? bytes, string expected) => Display.Bytes(bytes).ShouldBe(expected);

    [Theory]
    [InlineData(null, "–")]
    [InlineData(0d, "–")]
    [InlineData(360830.66, "352.4 KB/s")]
    public void Speed(double? speed, string expected) => Display.Speed(speed).ShouldBe(expected);

    [Theory]
    [InlineData(null, "–")]
    [InlineData(35d, "0:35")]
    [InlineData(253d, "4:13")]
    [InlineData(3735d, "1:02:15")]
    [InlineData(36000d, "10:00:00")]
    public void Duration(double? seconds, string expected) =>
        Display.Duration(seconds is null ? null : TimeSpan.FromSeconds(seconds.Value)).ShouldBe(expected);

    [Theory]
    [InlineData(null, "–")]
    [InlineData(129.69, "130 kbps")]
    [InlineData(4400.5, "4.4 Mbps")]
    public void Bitrate(double? kbps, string expected) => Display.Bitrate(kbps).ShouldBe(expected);

    [Fact]
    public void Count_and_percent()
    {
        Display.Count(1234567).ShouldBe("1,234,567");
        Display.Percent(0.4567).ShouldBe("45.7%");
        Display.Percent(null).ShouldBe("–");
    }
}
