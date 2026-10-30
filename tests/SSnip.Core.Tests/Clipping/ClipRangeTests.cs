using Shouldly;
using SSnip.Core.Clipping;

namespace SSnip.Core.Tests.Clipping;

public class ClipRangeTests
{
    private static ClipMark Clip(double inSeconds, double outSeconds) =>
        new() { In = TimeSpan.FromSeconds(inSeconds), Out = TimeSpan.FromSeconds(outSeconds) };

    [Fact]
    public void Handles_widen_both_sides() =>
        Clip(10, 20).WithHandles(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60)).ShouldBe((TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(22)));

    [Fact]
    public void Handles_stop_at_the_start_and_end_of_the_source() =>
        Clip(1, 59).WithHandles(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60)).ShouldBe((TimeSpan.Zero, TimeSpan.FromSeconds(60)));

    [Fact]
    public void Unknown_duration_does_not_limit_the_end() =>
        Clip(1, 59).WithHandles(TimeSpan.FromSeconds(2), null).End.ShouldBe(TimeSpan.FromSeconds(61));

    [Fact]
    public void Length_and_validity()
    {
        Clip(10, 12.5).Length.ShouldBe(TimeSpan.FromSeconds(2.5));
        Clip(10, 10).IsValid.ShouldBeFalse();
        Clip(-1, 10).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Section_is_millisecond_exact() =>
        ClipTime.Section(new TimeSpan(0, 0, 12, 30, 250), new TimeSpan(0, 1, 3, 0, 5)).ShouldBe("*0:12:30.250-1:03:00.005");

    [Theory]
    [InlineData(750.25, "12:30.250")]
    [InlineData(3735, "1:02:15.000")]
    [InlineData(5.5, "0:05.500")]
    public void Display(double seconds, string expected) => ClipTime.Display(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);

    [Fact]
    public void Seconds_for_ffmpeg() => ClipTime.Seconds(TimeSpan.FromMilliseconds(12345)).ShouldBe("12.345");
}
