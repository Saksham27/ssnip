using Shouldly;
using SSnip.Core.Media;

namespace SSnip.Core.Tests.Media;

public class PlaylistSelectionTests
{
    private static PlaylistEntry Entry(string title, int? seconds = null) =>
        new("TestVid0001", title, "https://www.example.com/v", seconds is null ? null : TimeSpan.FromSeconds(seconds.Value), null);

    [Theory]
    [InlineData("recap", "COD MW Recap Part 2", true)]
    [InlineData("RECAP", "cod mw recap", true)]
    [InlineData("trailer", "COD MW Recap", false)]
    [InlineData("/part \\d+$/", "Recap Part 12", true)]
    [InlineData("/^part/", "Recap Part 12", false)]
    [InlineData("रीकैप", "मॉडर्न वॉरफेयर रीकैप", true)]
    [InlineData("", "anything", true)]
    public void Title_filter(string filter, string title, bool matches) =>
        new PlaylistFilter(filter).Matches(Entry(title)).ShouldBe(matches);

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(600, true)]
    [InlineData(601, false)]
    [InlineData(null, true)]
    public void Duration_limits_let_unknown_durations_through(int? seconds, bool matches) =>
        new PlaylistFilter(MinDuration: TimeSpan.FromMinutes(1), MaxDuration: TimeSpan.FromMinutes(10)).Matches(Entry("x", seconds)).ShouldBe(matches);

    [Fact]
    public void Bad_regex_is_reported_not_thrown() =>
        new PlaylistFilter("/(unclosed/").Validate().ShouldNotBeNull();

    [Fact]
    public void Reversed_durations_are_reported() =>
        new PlaylistFilter(MinDuration: TimeSpan.FromMinutes(5), MaxDuration: TimeSpan.FromMinutes(1)).Validate().ShouldNotBeNull();

    [Fact]
    public void Empty_filter() => new PlaylistFilter(" ").IsEmpty.ShouldBeTrue();

    [Theory]
    [InlineData("1:3", new[] { 1, 2, 3 })]
    [InlineData("1:10,15", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15 })]
    [InlineData("20-22", new[] { 20, 21, 22 })]
    [InlineData("18:", new[] { 18, 19, 20, 21, 22, 23, 24, 25 })]
    [InlineData(":2", new[] { 1, 2 })]
    [InlineData("-3:", new[] { 23, 24, 25 })]
    [InlineData("-1", new[] { 25 })]
    [InlineData("5, 3, 5", new[] { 3, 5 })]
    [InlineData("24:99", new[] { 24, 25 })]
    [InlineData("30", new int[0])]
    [InlineData("", new int[0])]
    public void Item_lists(string spec, int[] expected) =>
        PlaylistItems.Parse(spec, 25).ShouldBe(expected);

    [Theory]
    [InlineData("a")]
    [InlineData("1:2:3")]
    [InlineData("1-")]
    [InlineData("1..3")]
    public void Bad_item_lists_throw(string spec) =>
        Should.Throw<FormatException>(() => PlaylistItems.Parse(spec, 25));

    [Theory]
    [InlineData("https://www.youtube.com/@JemmaLive", ChannelTab.Shorts, "https://www.youtube.com/@JemmaLive/shorts")]
    [InlineData("https://www.youtube.com/@JemmaLive/videos", ChannelTab.Live, "https://www.youtube.com/@JemmaLive/streams")]
    [InlineData("https://youtube.com/channel/UC0000000000000000000000/featured", ChannelTab.Videos, "https://www.youtube.com/channel/UC0000000000000000000000/videos")]
    [InlineData("https://www.youtube.com/c/Example/", ChannelTab.Videos, "https://www.youtube.com/c/Example/videos")]
    public void Youtube_channel_tabs(string url, ChannelTab tab, string expected) =>
        ChannelTabs.UrlFor(url, tab).ShouldBe(expected);

    [Theory]
    [InlineData("https://www.youtube.com/@JemmaLive", null)]
    [InlineData("https://www.youtube.com/@JemmaLive/featured", null)]
    [InlineData("https://www.youtube.com/@JemmaLive/videos", ChannelTab.Videos)]
    [InlineData("https://www.youtube.com/@JemmaLive/shorts/", ChannelTab.Shorts)]
    [InlineData("https://www.youtube.com/@JemmaLive/streams", ChannelTab.Live)]
    [InlineData("https://www.youtube.com/watch?v=TestVid0001", null)]
    public void Tab_of_a_channel_link(string url, ChannelTab? expected) =>
        ChannelTabs.TabOf(url).ShouldBe(expected);

    [Fact]
    public void Only_channel_links_are_channels()
    {
        ChannelTabs.IsChannel("https://www.youtube.com/@JemmaLive").ShouldBeTrue();
        ChannelTabs.IsChannel("https://www.youtube.com/playlist?list=PL0000").ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PL0000")]
    [InlineData("https://www.youtube.com/watch?v=TestVid0001")]
    [InlineData("https://www.example.com/@name")]
    public void Non_channel_links_have_no_tabs(string url) =>
        ChannelTabs.UrlFor(url, ChannelTab.Videos).ShouldBeNull();
}
