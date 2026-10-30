using Shouldly;
using SSnip.Core.Clipping;

namespace SSnip.Core.Tests.Clipping;

public class TimestampParserTests
{
    private static TimeSpan T(int h, int m, int s, int ms = 0) => new(0, h, m, s, ms);

    [Fact]
    public void Plan_examples_become_named_clips()
    {
        var clips = TimestampParser.ParseLines("12:30-13:10 Soap scene\n1:02:15 - 1:03:00 Final mission");

        clips.Count.ShouldBe(2);
        clips[0].ShouldBe(new ClipMark { In = T(0, 12, 30), Out = T(0, 13, 10), Name = "Soap scene" });
        clips[1].ShouldBe(new ClipMark { In = T(1, 2, 15), Out = T(1, 3, 0), Name = "Final mission" });
    }

    [Theory]
    [InlineData("Soap scene 12:30–13:10")]
    [InlineData("[12:30 → 13:10] Soap scene")]
    [InlineData("12:30 to 13:10 - Soap scene")]
    [InlineData("(12:30-13:10) Soap scene")]
    public void Other_range_styles(string line)
    {
        var clip = TimestampParser.ParseLines(line).ShouldHaveSingleItem();

        clip.In.ShouldBe(T(0, 12, 30));
        clip.Out.ShouldBe(T(0, 13, 10));
        clip.Name.ShouldBe("Soap scene");
    }

    [Fact]
    public void Milliseconds_are_kept() =>
        TimestampParser.ParseLines("0:05.250-0:07.5 Jump").Single().ShouldBe(new ClipMark { In = T(0, 0, 5, 250), Out = T(0, 0, 7, 500), Name = "Jump" });

    [Fact]
    public void Hindi_names_stay_intact() =>
        TimestampParser.ParseLines("2:00-2:30 अंतिम मिशन").Single().Name.ShouldBe("अंतिम मिशन");

    [Fact]
    public void Unnamed_ranges_get_numbers() =>
        TimestampParser.ParseLines("1:00-1:10\n2:00-2:10").Select(c => c.Name).ShouldBe(["Clip 1", "Clip 2"]);

    [Fact]
    public void Single_times_run_to_the_next_one_and_the_last_to_the_end()
    {
        var clips = TimestampParser.ScanDescription("Chapters:\n0:00 Intro\n1:15 Boss fight\nThanks for watching!\n4:02 Outro", T(0, 5, 0));

        clips.Select(c => (c.Name, c.In, c.Out)).ShouldBe([
            ("Intro", T(0, 0, 0), T(0, 1, 15)),
            ("Boss fight", T(0, 1, 15), T(0, 4, 2)),
            ("Outro", T(0, 4, 2), T(0, 5, 0)),
        ]);
    }

    [Fact]
    public void Last_single_time_without_a_duration_is_dropped() =>
        TimestampParser.ScanDescription("0:00 Intro\n1:15 Boss", null).Select(c => c.Name).ShouldBe(["Intro"]);

    [Theory]
    [InlineData("Version 1.2.3 is out")]
    [InlineData("Call 555-1234")]
    [InlineData("Score 3:2 at half time")]
    [InlineData("")]
    public void Lines_without_times_are_skipped(string line) =>
        TimestampParser.ParseLines(line, T(1, 0, 0)).ShouldBeEmpty();

    [Fact]
    public void Ranges_that_end_before_they_start_are_skipped() =>
        TimestampParser.ParseLines("13:10-12:30 Backwards").ShouldBeEmpty();

    [Theory]
    [InlineData("12:30", 750)]
    [InlineData("1:02:15", 3735)]
    [InlineData("0:05.5", 5.5)]
    [InlineData("01:00:00.125", 3600.125)]
    public void Times(string text, double seconds) =>
        TimestampParser.Time(text).ShouldBe(TimeSpan.FromSeconds(seconds));

    [Theory]
    [InlineData("12")]
    [InlineData("1:60")]
    [InlineData("1.5:00")]
    [InlineData("1:2:3:4")]
    [InlineData("a:bc")]
    public void Not_times(string text) => TimestampParser.Time(text).ShouldBeNull();
}
