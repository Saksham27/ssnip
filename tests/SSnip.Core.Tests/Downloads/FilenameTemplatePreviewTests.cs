using Shouldly;
using SSnip.Core.Downloads;
using SSnip.Core.Media;

namespace SSnip.Core.Tests.Downloads;

public class FilenameTemplatePreviewTests
{
    private static readonly MediaInfo Video = MediaInfoParser.Parse(Fixtures.Read("video.json"));

    [Fact]
    public void Default_template_uses_title_and_id() =>
        FilenameTemplatePreview.Render(FilenameTemplates.Default, Video, "mp4").ShouldBe("मॉडर्न वॉरफेयर रीकैप ｜ Part 1 [TestVid0001].mp4");

    [Fact]
    public void Date_and_channel_fields()
    {
        FilenameTemplatePreview.Render("%(upload_date)s - %(title)s [%(id)s].%(ext)s", Video, "mkv")
            .ShouldBe("20260901 - मॉडर्न वॉरफेयर रीकैप ｜ Part 1 [TestVid0001].mkv");
        FilenameTemplatePreview.Render("%(channel)s - %(id)s.%(ext)s", Video, "wav").ShouldBe("Example Channel - TestVid0001.wav");
    }

    [Fact]
    public void Unknown_and_missing_fields_become_NA() =>
        FilenameTemplatePreview.Render("%(view_count)s %(upload_date)s", new MediaInfo { Kind = MediaKind.Video, Id = "x", Title = "t" }, "mp4").ShouldBe("NA NA");

    [Theory]
    [InlineData("AC/DC", "AC／DC")]
    [InlineData(@"a\b", "a＼b")]
    [InlineData("Who? What*", "Who？ What＊")]
    [InlineData("12:30 <Soap> \"Final\"", "12：30 ＜Soap＞ ＂Final＂")]
    [InlineData("tab\there", "tab here")]
    [InlineData("जब रोटी सब्जी", "जब रोटी सब्जी")]
    public void Sanitises_like_windows_filenames(string value, string expected) =>
        FilenameTemplatePreview.SanitizeForWindows(value).ShouldBe(expected);

    [Fact]
    public void Template_text_itself_is_not_sanitised() =>
        FilenameTemplatePreview.Render("clips/%(id)s.%(ext)s", Video, "mp4").ShouldBe("clips/TestVid0001.mp4");
}
