using Shouldly;
using SSnip.Core.Clipping;
using SSnip.Core.Downloads;

namespace SSnip.Core.Tests.Downloads;

public class YtDlpArgsBuilderClipTests
{
    private static DownloadOptions Options() =>
        new() { Url = "https://www.example.com/watch?v=TestVid0001", OutputDirectory = @"E:\Footage\Inbox\clips" };

    private static List<string> Build(DownloadOptions options) => [.. YtDlpArgsBuilder.Build(options)];

    [Fact]
    public void Accurate_sections_force_keyframes_at_the_cuts()
    {
        var args = Build(Options() with { Sections = [ClipTime.Section(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20))], CutMode = CutMode.Accurate });

        args.ShouldContain("*0:00:10.000-0:00:20.000");
        args.ShouldContain("--force-keyframes-at-cuts");
    }

    [Fact]
    public void Fast_sections_cut_on_keyframes() =>
        Build(Options() with { Sections = ["*0:10-0:20"], CutMode = CutMode.Fast }).ShouldNotContain("--force-keyframes-at-cuts");

    [Fact]
    public void Cut_mode_means_nothing_without_sections() =>
        Build(Options() with { CutMode = CutMode.Accurate }).ShouldNotContain("--force-keyframes-at-cuts");

    [Fact]
    public void Split_chapters_names_each_chapter_file()
    {
        var args = Build(Options() with { SplitChapters = true });

        args.ShouldContain("--split-chapters");
        args.ShouldContain(YtDlpArgsBuilder.ChapterTemplate);
    }

    [Fact]
    public void The_editor_profile_is_not_a_yt_dlp_option() =>
        Build(Options() with { Profile = new EditorProfile(EditorProfileKind.H264, "h264_amf") })
            .ShouldBe(Build(Options()));

    [Theory]
    [InlineData("Soap scene", "Soap scene [%(id)s].%(ext)s")]
    [InlineData("100% headshot", "100%% headshot [%(id)s].%(ext)s")]
    [InlineData("  ", "Clip [%(id)s].%(ext)s")]
    [InlineData("अंतिम मिशन", "अंतिम मिशन [%(id)s].%(ext)s")]
    public void Clip_templates_escape_percent(string name, string expected) =>
        YtDlpArgsBuilder.ClipTemplate(name).ShouldBe(expected);
}
