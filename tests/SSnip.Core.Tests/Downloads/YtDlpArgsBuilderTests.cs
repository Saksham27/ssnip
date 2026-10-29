using Shouldly;
using SSnip.Core.Downloads;

namespace SSnip.Core.Tests.Downloads;

public class YtDlpArgsBuilderTests
{
    private const string Url = "https://www.example.com/watch?v=TestVid0001";

    private static DownloadOptions Options(FormatPreset preset = FormatPreset.EditorSafe) =>
        new() { Url = Url, OutputDirectory = @"E:\Footage\Inbox\raw", Preset = preset };

    private static List<string> Build(DownloadOptions options) => [.. YtDlpArgsBuilder.Build(options)];

    /// <summary>The arguments that follow a flag.</summary>
    private static string ValueOf(IReadOnlyList<string> args, string flag)
    {
        var index = args.ToList().IndexOf(flag);
        index.ShouldBeGreaterThanOrEqualTo(0, $"{flag} missing");
        return args[index + 1];
    }

    [Fact]
    public void Always_streams_progress_and_prints_the_final_path()
    {
        var args = Build(Options());

        args.ShouldContain("--newline");
        args.ShouldContain("--progress");
        args.ShouldContain("--no-playlist");
        args.ShouldContain("download:FSPROG %(progress)j");
        args.ShouldContain("postprocess:FSPP %(progress)j");
        ValueOf(args, "--print").ShouldBe("after_move:FSFILE %(filepath)s");
    }

    [Fact]
    public void Url_comes_last_after_double_dash()
    {
        var args = Build(Options() with { Url = "  " + Url + " " });

        args[^2].ShouldBe("--");
        args[^1].ShouldBe(Url);
    }

    [Fact]
    public void A_url_that_looks_like_an_option_stays_a_url()
    {
        var args = Build(Options() with { Url = "-flag-lookalike" });

        args[^2].ShouldBe("--");
        args[^1].ShouldBe("-flag-lookalike");
    }

    [Fact]
    public void Windows_safe_filenames_by_default()
    {
        var args = Build(Options());

        args.ShouldContain("--windows-filenames");
        ValueOf(args, "--trim-filenames").ShouldBe("150");
    }

    [Fact]
    public void Output_folder_and_template_are_separate_arguments()
    {
        var args = Build(Options() with { OutputDirectory = @"E:\Footage\COD MW Recap\raw", FilenameTemplate = "%(upload_date)s - %(title)s [%(id)s].%(ext)s" });

        ValueOf(args, "-P").ShouldBe(@"E:\Footage\COD MW Recap\raw");
        ValueOf(args, "-o").ShouldBe("%(upload_date)s - %(title)s [%(id)s].%(ext)s");
    }

    [Fact]
    public void Default_network_settings()
    {
        var args = Build(Options());

        ValueOf(args, "-N").ShouldBe("4");
        ValueOf(args, "--retries").ShouldBe("10");
        ValueOf(args, "--fragment-retries").ShouldBe("10");
        args.ShouldNotContain("--limit-rate");
    }

    [Fact]
    public void Editor_safe_prefers_h264_and_aac_in_mp4()
    {
        var args = Build(Options(FormatPreset.EditorSafe));

        ValueOf(args, "-S").ShouldBe("vcodec:h264,res,fps,acodec:aac");
        ValueOf(args, "--merge-output-format").ShouldBe("mp4");
        args.ShouldNotContain("-f");
    }

    [Fact]
    public void Max_quality_takes_best_streams_into_mkv()
    {
        var args = Build(Options(FormatPreset.MaxQuality));

        ValueOf(args, "-f").ShouldBe("bv*+ba/b");
        ValueOf(args, "--merge-output-format").ShouldBe("mkv");
    }

    [Fact]
    public void Cap_1080_sorts_by_resolution_limit()
    {
        var args = Build(Options(FormatPreset.Cap1080));

        ValueOf(args, "-S").ShouldBe("res:1080,fps");
        args.ShouldNotContain("--merge-output-format");
    }

    [Theory]
    [InlineData(AudioFormat.Wav, "wav")]
    [InlineData(AudioFormat.Mp3, "mp3")]
    [InlineData(AudioFormat.M4a, "m4a")]
    [InlineData(AudioFormat.Opus, "opus")]
    [InlineData(AudioFormat.Flac, "flac")]
    public void Audio_only_extracts_best_audio(AudioFormat format, string expected)
    {
        var args = Build(Options(FormatPreset.AudioOnly) with { AudioFormat = format });

        ValueOf(args, "-f").ShouldBe("ba/b");
        args.ShouldContain("-x");
        ValueOf(args, "--audio-format").ShouldBe(expected);
        ValueOf(args, "--audio-quality").ShouldBe("0");
        args.ShouldNotContain("--merge-output-format");
    }

    [Fact]
    public void Preview_proxy_is_360p_h264()
    {
        var args = Build(Options(FormatPreset.PreviewProxy));

        ValueOf(args, "-f").ShouldBe("bv*[height<=360]+ba/b[height<=360]");
        ValueOf(args, "-S").ShouldBe("vcodec:h264");
    }

    [Fact]
    public void Custom_video_and_audio_pair()
    {
        var args = Build(Options(FormatPreset.Custom) with { Custom = new FormatSelection("137", "140"), MergeContainer = "mkv" });

        ValueOf(args, "-f").ShouldBe("137+140");
        ValueOf(args, "--merge-output-format").ShouldBe("mkv");
        args.ShouldNotContain("-S");
    }

    [Theory]
    [InlineData("18", null, "18")]
    [InlineData(null, "251", "251")]
    [InlineData(" 137 ", " 140 ", "137+140")]
    public void Custom_single_formats(string? video, string? audio, string expected) =>
        ValueOf(Build(Options(FormatPreset.Custom) with { Custom = new FormatSelection(video, audio) }), "-f").ShouldBe(expected);

    [Fact]
    public void Custom_sort_string_without_format()
    {
        var args = Build(Options(FormatPreset.Custom) with { Custom = new FormatSelection(null, null, "res:1440,fps,vcodec:av01") });

        ValueOf(args, "-S").ShouldBe("res:1440,fps,vcodec:av01");
        args.ShouldNotContain("-f");
    }

    [Fact]
    public void Custom_without_selection_throws() =>
        Should.Throw<ArgumentException>(() => Build(Options(FormatPreset.Custom) with { Custom = new FormatSelection(" ", null) }));

    [Fact]
    public void Container_override_applies_to_presets() =>
        ValueOf(Build(Options(FormatPreset.EditorSafe) with { MergeContainer = "mov" }), "--merge-output-format").ShouldBe("mov");

    [Fact]
    public void Unknown_container_throws() =>
        Should.Throw<ArgumentException>(() => Build(Options() with { MergeContainer = "avi" }));

    [Fact]
    public void Skip_existing_by_default_overwrite_when_asked()
    {
        Build(Options()).ShouldContain("--no-overwrites");
        Build(Options() with { Overwrite = true }).ShouldContain("--force-overwrites");
        Build(Options() with { Overwrite = true }).ShouldNotContain("--no-overwrites");
    }

    [Theory]
    [InlineData("5M")]
    [InlineData("500K")]
    [InlineData("1.5m")]
    [InlineData("2048")]
    public void Rate_limit_is_passed(string rate) =>
        ValueOf(Build(Options() with { RateLimit = rate }), "--limit-rate").ShouldBe(rate);

    [Theory]
    [InlineData("fast")]
    [InlineData("5 MB")]
    [InlineData("-5M")]
    public void Bad_rate_limit_throws(string rate) =>
        Should.Throw<ArgumentException>(() => Build(Options() with { RateLimit = rate }));

    [Fact]
    public void Fragments_and_retries_are_configurable()
    {
        var args = Build(Options() with { ConcurrentFragments = 8, Retries = 3, TrimFilenames = 100 });

        ValueOf(args, "-N").ShouldBe("8");
        ValueOf(args, "--retries").ShouldBe("3");
        ValueOf(args, "--trim-filenames").ShouldBe("100");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void Fragment_count_out_of_range_throws(int fragments) =>
        Should.Throw<ArgumentException>(() => Build(Options() with { ConcurrentFragments = fragments }));

    [Fact]
    public void One_download_sections_argument_per_range()
    {
        var args = Build(Options() with { Sections = ["*00:12:30-00:13:10", "*1:02:15-1:03:00"] });

        args.Where(a => a == "--download-sections").Count().ShouldBe(2);
        args.ShouldContain("*00:12:30-00:13:10");
        args.ShouldContain("*1:02:15-1:03:00");
    }

    [Theory]
    [InlineData("", @"E:\x", "%(title)s.%(ext)s")]
    [InlineData(Url, "", "%(title)s.%(ext)s")]
    [InlineData(Url, @"E:\x", " ")]
    public void Missing_required_fields_throw(string url, string folder, string template) =>
        Should.Throw<ArgumentException>(() => Build(new DownloadOptions { Url = url, OutputDirectory = folder, FilenameTemplate = template }));

    [Fact]
    public void Hindi_template_and_folder_stay_intact()
    {
        var args = Build(Options() with { OutputDirectory = @"E:\Footage\रीकैप", FilenameTemplate = "मॉडर्न वॉरफेयर %(id)s.%(ext)s" });

        ValueOf(args, "-P").ShouldBe(@"E:\Footage\रीकैप");
        ValueOf(args, "-o").ShouldBe("मॉडर्न वॉरफेयर %(id)s.%(ext)s");
    }

    [Fact]
    public void Is_a_pure_function() =>
        Build(Options(FormatPreset.MaxQuality)).ShouldBe(Build(Options(FormatPreset.MaxQuality)));
}
