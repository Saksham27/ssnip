using Shouldly;
using SSnip.Core.Media;

namespace SSnip.Core.Tests.Media;

public class MediaInfoParserTests
{
    private static readonly MediaInfo Video = MediaInfoParser.Parse(Fixtures.Read("video.json"));

    [Fact]
    public void Reads_the_info_panel_fields()
    {
        Video.Kind.ShouldBe(MediaKind.Video);
        Video.Id.ShouldBe("TestVid0001");
        Video.Title.ShouldBe("मॉडर्न वॉरफेयर रीकैप | Part 1");
        Video.Channel.ShouldBe("Example Channel");
        Video.ChannelUrl.ShouldBe("https://www.example.com/@example");
        Video.Duration.ShouldBe(TimeSpan.FromSeconds(253));
        Video.UploadDate.ShouldBe(new DateOnly(2026, 9, 1));
        Video.ViewCount.ShouldBe(12345);
        Video.LiveStatus.ShouldBe("not_live");
        Video.Description!.ShouldContain("Soap scene");
        Video.IsSingleVideo.ShouldBeTrue();
    }

    [Fact]
    public void Picks_the_widest_jpeg_thumbnail_over_webp() =>
        Video.ThumbnailUrl.ShouldBe("https://img.example.com/vi/TestVid0001/hqdefault.jpg?sqp=abc");

    [Fact]
    public void Reads_chapters()
    {
        Video.Chapters.Count.ShouldBe(2);
        Video.Chapters[1].ShouldBe(new ChapterInfo("Final mission", TimeSpan.FromSeconds(90.5), TimeSpan.FromSeconds(253)));
        Video.Chapters[0].Length.ShouldBe(TimeSpan.FromSeconds(90.5));
    }

    [Fact]
    public void Separates_manual_and_automatic_subtitles_and_skips_live_chat()
    {
        Video.Subtitles.Where(s => !s.IsAutomatic).Select(s => s.Language).ShouldBe(["en"]);
        Video.Subtitles.Where(s => s.IsAutomatic).Select(s => s.Language).ShouldBe(["hi", "en-orig"], ignoreOrder: true);
        Video.Subtitles.Single(s => s.Language == "en").Name.ShouldBe("English");
    }

    [Fact]
    public void Reads_every_format()
    {
        Video.Formats.Count.ShouldBe(9);

        var f137 = Video.FindFormat("137")!;
        f137.Resolution.ShouldBe("1920x1080");
        f137.VideoCodec.ShouldBe("avc1.640028");
        f137.AudioCodec.ShouldBeNull();
        f137.IsVideoOnly.ShouldBeTrue();
        f137.FileSize.ShouldBe(98765432);
        f137.IsFileSizeApproximate.ShouldBeFalse();
        f137.Bitrate.ShouldBe(4400.5);
    }

    [Fact]
    public void Audio_formats_are_audio_only()
    {
        var f140 = Video.FindFormat("140")!;
        f140.IsAudioOnly.ShouldBeTrue();
        f140.Resolution.ShouldBe("audio only");
        f140.Language.ShouldBe("hi");

        // HLS audio with no codec listed is still audio.
        Video.FindFormat("233")!.IsAudioOnly.ShouldBeTrue();
    }

    [Fact]
    public void Combined_format_has_video_and_audio()
    {
        var f18 = Video.FindFormat("18")!;
        f18.HasVideo.ShouldBeTrue();
        f18.HasAudio.ShouldBeTrue();
    }

    [Fact]
    public void Approximate_size_is_used_and_flagged()
    {
        var f251 = Video.FindFormat("251")!;
        f251.FileSize.ShouldBe(794214);
        f251.IsFileSizeApproximate.ShouldBeTrue();
        Video.FindFormat("616")!.FileSize.ShouldBeNull();
    }

    [Fact]
    public void Flags_hdr_premium_and_storyboards()
    {
        Video.FindFormat("337")!.IsHdr.ShouldBeTrue();
        Video.FindFormat("137")!.IsHdr.ShouldBeFalse();
        Video.FindFormat("616")!.IsPremium.ShouldBeTrue();
        Video.FindFormat("137")!.IsPremium.ShouldBeFalse();
        var storyboard = Video.FindFormat("sb0")!;
        storyboard.IsStoryboard.ShouldBeTrue();
        storyboard.HasVideo.ShouldBeFalse();
        storyboard.HasAudio.ShouldBeFalse();
    }

    [Fact]
    public void Unknown_codecs_with_a_height_count_as_video()
    {
        var hls = Video.FindFormat("hls-720")!;
        hls.HasVideo.ShouldBeTrue();
        hls.Resolution.ShouldBe("720p");
    }

    [Fact]
    public void Channel_listing_is_a_channel_with_entries()
    {
        var channel = MediaInfoParser.Parse(Fixtures.Read("channel.json"));

        channel.Kind.ShouldBe(MediaKind.Channel);
        channel.IsSingleVideo.ShouldBeFalse();
        channel.Entries.Count.ShouldBe(2);
        channel.Entries[0].ShouldBe(new PlaylistEntry("TestVid0001", "First upload", "https://www.example.com/watch?v=TestVid0001", TimeSpan.FromSeconds(61), null));
        channel.Entries[1].Duration.ShouldBeNull();
        channel.Formats.ShouldBeEmpty();
    }

    [Fact]
    public void Plain_playlist_is_a_playlist() =>
        MediaInfoParser.Parse("""{ "_type": "playlist", "id": "PLabc123", "title": "Recap", "webpage_url": "https://www.example.com/playlist?list=PLabc123", "entries": [] }""")
            .Kind.ShouldBe(MediaKind.Playlist);

    [Theory]
    [InlineData("is_live", MediaKind.Live)]
    [InlineData("is_upcoming", MediaKind.Upcoming)]
    [InlineData("was_live", MediaKind.Video)]
    public void Live_status_sets_the_kind(string status, MediaKind kind) =>
        MediaInfoParser.Parse($$"""{ "id": "x", "title": "t", "live_status": "{{status}}" }""").Kind.ShouldBe(kind);

    [Fact]
    public void Missing_fields_are_null_not_errors()
    {
        var info = MediaInfoParser.Parse("""{ "id": "x" }""");

        info.Title.ShouldBe("x");
        info.Duration.ShouldBeNull();
        info.UploadDate.ShouldBeNull();
        info.ThumbnailUrl.ShouldBeNull();
        info.Formats.ShouldBeEmpty();
    }

    [Fact]
    public void Invalid_json_throws() =>
        Should.Throw<System.Text.Json.JsonException>(() => MediaInfoParser.Parse("ERROR: not json"));
}
