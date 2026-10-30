using Shouldly;
using SSnip.Core.Media;

namespace SSnip.Core.Tests.Media;

public class SubtitlePickerTests
{
    private static readonly string[] EnglishThenHindi = ["en", "hi"];

    private static SubtitleTrack Manual(string language) => new(language, null, false);

    private static SubtitleTrack Auto(string language) => new(language, null, true);

    [Fact]
    public void Manual_track_in_priority_order_wins() =>
        SubtitlePicker.Pick(EnglishThenHindi, [Manual("hi"), Manual("en"), Auto("en-orig")], true).ShouldBe("en");

    [Fact]
    public void Lower_priority_manual_beats_auto() =>
        SubtitlePicker.Pick(EnglishThenHindi, [Manual("hi"), Auto("en-orig"), Auto("en")], true).ShouldBe("hi");

    [Fact]
    public void Original_language_auto_captions_beat_translations() =>
        SubtitlePicker.Pick(EnglishThenHindi, [Auto("hi-orig"), Auto("hi"), Auto("en")], true).ShouldBe("hi");

    [Fact]
    public void English_original_is_picked_over_hindi_translation() =>
        SubtitlePicker.Pick(EnglishThenHindi, [Auto("en-orig"), Auto("en"), Auto("hi")], true).ShouldBe("en");

    [Fact]
    public void Falls_back_to_any_auto_track() =>
        SubtitlePicker.Pick(EnglishThenHindi, [Auto("fr-orig"), Auto("hi")], true).ShouldBe("hi");

    [Fact]
    public void Manual_only_returns_nothing_without_manual_tracks() =>
        SubtitlePicker.Pick(EnglishThenHindi, [Auto("en-orig"), Auto("en")], false).ShouldBeNull();

    [Fact]
    public void Unknown_tracks_use_the_first_language() =>
        SubtitlePicker.Pick(EnglishThenHindi, [], true).ShouldBe("en");

    [Fact]
    public void No_priority_no_language() =>
        SubtitlePicker.Pick([], [Manual("en")], true).ShouldBeNull();
}
