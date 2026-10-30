using Shouldly;
using SSnip.Core.Downloads;

namespace SSnip.Core.Tests.Downloads;

/// <summary>Subtitles, metadata, SponsorBlock, filters and live options.</summary>
public class YtDlpArgsBuilderExtrasTests
{
    private const string Url = "https://www.example.com/watch?v=TestVid0001";

    private static DownloadOptions Options(FormatPreset preset = FormatPreset.EditorSafe) =>
        new() { Url = Url, OutputDirectory = @"E:\Footage\Inbox\raw", Preset = preset };

    private static List<string> Build(DownloadOptions options) => [.. YtDlpArgsBuilder.Build(options)];

    /// <summary>Every value that follows <paramref name="flag"/>, for flags used more than once.</summary>
    private static List<string> ValuesOf(List<string> args, string flag) =>
        [.. args.Select((a, i) => (a, i)).Where(x => x.a == flag && x.i + 1 < args.Count).Select(x => args[x.i + 1])];

    private static string ValueOf(List<string> args, string flag) => ValuesOf(args, flag).ShouldHaveSingleItem();

    [Fact]
    public void No_extras_by_default()
    {
        var args = Build(Options());

        args.ShouldNotContain(a => a.StartsWith("--write-", StringComparison.Ordinal));
        args.ShouldNotContain(a => a.StartsWith("--embed-", StringComparison.Ordinal));
        args.ShouldNotContain(a => a.StartsWith("--sponsorblock", StringComparison.Ordinal));
        args.ShouldNotContain("--live-from-start");
    }

    [Fact]
    public void Srt_subtitles_stay_as_separate_files()
    {
        var args = Build(Options() with { Subtitles = new SubtitleOptions(["en", "hi"]) });

        args.ShouldContain("--write-subs");
        args.ShouldContain("--write-auto-subs");
        ValueOf(args, "--sub-langs").ShouldBe("en,hi");
        ValueOf(args, "--convert-subs").ShouldBe("srt");
        args.ShouldNotContain("--embed-subs");
    }

    [Fact]
    public void Manual_only_subtitles_skip_auto_captions() =>
        Build(Options() with { Subtitles = new SubtitleOptions(["en"], IncludeAutomatic: false) }).ShouldNotContain("--write-auto-subs");

    [Fact]
    public void Embedded_only_subtitles_do_not_keep_the_srt()
    {
        var args = Build(Options() with { Subtitles = new SubtitleOptions(["en"], Mode: SubtitleMode.Embed) });

        args.ShouldContain("--embed-subs");
        ValueOf(args, "--compat-options").ShouldBe("no-keep-subs");
    }

    [Fact]
    public void Srt_and_embed_keeps_both()
    {
        var args = Build(Options() with { Subtitles = new SubtitleOptions(["en"], Mode: SubtitleMode.SrtAndEmbed) });

        args.ShouldContain("--embed-subs");
        args.ShouldNotContain("--compat-options");
    }

    [Theory]
    [InlineData("en.*")]
    [InlineData("en,hi")]
    [InlineData("e")]
    [InlineData("")]
    public void Subtitle_languages_must_be_plain_codes(string language) =>
        Should.Throw<ArgumentException>(() => Build(Options() with { Subtitles = new SubtitleOptions([language]) }));

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-orig")]
    [InlineData("hi")]
    public void Regional_subtitle_codes_are_allowed(string language) =>
        ValueOf(Build(Options() with { Subtitles = new SubtitleOptions([language]) }), "--sub-langs").ShouldBe(language);

    [Fact]
    public void Embeds_metadata_thumbnail_and_chapters()
    {
        var args = Build(Options() with { EmbedMetadata = true, EmbedThumbnail = true, EmbedChapters = true });

        args.ShouldContain("--embed-metadata");
        args.ShouldContain("--embed-thumbnail");
        args.ShouldContain("--embed-chapters");
    }

    [Fact]
    public void Wav_never_gets_a_thumbnail_because_it_cannot_hold_one() =>
        Build(Options(FormatPreset.AudioOnly) with { AudioFormat = AudioFormat.Wav, EmbedThumbnail = true }).ShouldNotContain("--embed-thumbnail");

    [Fact]
    public void Mp3_gets_a_thumbnail() =>
        Build(Options(FormatPreset.AudioOnly) with { AudioFormat = AudioFormat.Mp3, EmbedThumbnail = true }).ShouldContain("--embed-thumbnail");

    [Fact]
    public void Info_json_goes_to_the_data_folder_named_by_site_and_id()
    {
        var args = Build(Options() with { InfoJsonDirectory = @"C:\Data\info" });

        args.ShouldContain("--write-info-json");
        ValuesOf(args, "-P").ShouldBe([@"E:\Footage\Inbox\raw", @"infojson:C:\Data\info"]);
        ValuesOf(args, "-o").ShouldBe([FilenameTemplates.Default, "infojson:%(extractor)s-%(id)s"]);
    }

    [Fact]
    public void Comments_and_description_go_with_the_info_json()
    {
        var args = Build(Options() with { InfoJsonDirectory = @"C:\Data\info", MaxComments = 200 });

        args.ShouldContain("--write-comments");
        args.ShouldContain("--write-description");
        ValueOf(args, "--extractor-args").ShouldBe("youtube:max_comments=200");
        ValuesOf(args, "-P").ShouldContain(@"description:C:\Data\info");
    }

    [Theory]
    [InlineData(null, 200)]
    [InlineData(@"C:\Data\info", 0)]
    public void Comments_need_an_info_folder_and_a_limit(string? infoDirectory, int maxComments) =>
        Should.Throw<ArgumentException>(() => Build(Options() with { InfoJsonDirectory = infoDirectory, MaxComments = maxComments }));

    [Fact]
    public void Footage_default_removes_sponsors_and_marks_the_rest()
    {
        var args = Build(Options() with { SponsorBlock = SponsorBlockOptions.FootageDefault });

        ValueOf(args, "--sponsorblock-remove").ShouldBe("sponsor,selfpromo,interaction");
        ValueOf(args, "--sponsorblock-mark").ShouldBe("intro,outro,preview,music_offtopic,filler");
    }

    [Fact]
    public void A_category_in_both_lists_is_only_removed()
    {
        var args = Build(Options() with { SponsorBlock = new SponsorBlockOptions(["sponsor"], ["sponsor", "intro"]) });

        ValueOf(args, "--sponsorblock-remove").ShouldBe("sponsor");
        ValueOf(args, "--sponsorblock-mark").ShouldBe("intro");
    }

    [Fact]
    public void Proxies_only_mark_segments_so_timestamps_match_the_original()
    {
        var args = Build(Options(FormatPreset.PreviewProxy) with { SponsorBlock = SponsorBlockOptions.FootageDefault });

        args.ShouldNotContain("--sponsorblock-remove");
        ValueOf(args, "--sponsorblock-mark").Split(',').Length.ShouldBe(SponsorBlockOptions.Categories.Count);
    }

    [Fact]
    public void Section_downloads_only_mark_segments()
    {
        var args = Build(Options() with { Sections = ["*0:10-0:20"], SponsorBlock = SponsorBlockOptions.FootageDefault });

        args.ShouldNotContain("--sponsorblock-remove");
        ValueOf(args, "--sponsorblock-mark").ShouldContain("sponsor");
    }

    [Fact]
    public void Empty_sponsorblock_lists_add_nothing() =>
        Build(Options() with { SponsorBlock = new SponsorBlockOptions([], []) }).ShouldNotContain(a => a.StartsWith("--sponsorblock", StringComparison.Ordinal));

    [Fact]
    public void Unknown_sponsorblock_category_throws() =>
        Should.Throw<ArgumentException>(() => Build(Options() with { SponsorBlock = new SponsorBlockOptions(["ads"], []) }));

    [Fact]
    public void Upload_date_filters_use_yyyymmdd()
    {
        var args = Build(Options() with { DateAfter = new DateOnly(2025, 1, 31), DateBefore = new DateOnly(2026, 2, 1) });

        ValueOf(args, "--dateafter").ShouldBe("20250131");
        ValueOf(args, "--datebefore").ShouldBe("20260201");
    }

    [Fact]
    public void Reversed_date_range_throws() =>
        Should.Throw<ArgumentException>(() => Build(Options() with { DateAfter = new DateOnly(2026, 1, 2), DateBefore = new DateOnly(2026, 1, 1) }));

    [Fact]
    public void Live_from_start_and_waiting_for_a_scheduled_stream()
    {
        var args = Build(Options() with { Live = new LiveOptions(FromStart: true, WaitForVideoSeconds: 60) });

        args.ShouldContain("--live-from-start");
        ValueOf(args, "--wait-for-video").ShouldBe("60");
    }

    [Fact]
    public void Live_streams_skip_sponsorblock() =>
        Build(Options() with { Live = new LiveOptions(false), SponsorBlock = SponsorBlockOptions.FootageDefault })
            .ShouldNotContain(a => a.StartsWith("--sponsorblock", StringComparison.Ordinal));

    [Fact]
    public void Live_from_now_has_no_extra_flags()
    {
        var args = Build(Options() with { Live = new LiveOptions(FromStart: false) });

        args.ShouldNotContain("--live-from-start");
        args.ShouldNotContain("--wait-for-video");
    }

    [Fact]
    public void Extras_stay_before_the_url()
    {
        var args = Build(Options() with { Subtitles = new SubtitleOptions(["en"]), SponsorBlock = SponsorBlockOptions.FootageDefault, DateAfter = new DateOnly(2025, 1, 1) });

        args[^2].ShouldBe("--");
        args[^1].ShouldBe(Url);
    }
}
