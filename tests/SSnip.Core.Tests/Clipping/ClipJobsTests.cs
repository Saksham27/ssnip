using System.Text.Json;
using Shouldly;
using SSnip.Core.Clipping;
using SSnip.Core.Downloads;
using SSnip.Core.Queue;

namespace SSnip.Core.Tests.Clipping;

public class ClipJobsTests
{
    private static LocalCutOptions Cut(CutMode mode, EditorProfile? profile = null) => new()
    {
        InputPath = @"E:\raw\Source [TestVid0001].mkv",
        OutputDirectory = @"E:\clips",
        Start = TimeSpan.FromSeconds(10),
        End = TimeSpan.FromSeconds(20),
        Name = "Soap: scene?",
        Mode = mode,
        Profile = profile,
    };

    [Fact]
    public void Fast_cut_keeps_the_source_container() =>
        Cut(CutMode.Fast).OutputPath.ShouldBe(@"E:\clips\Soap_ scene_.mkv");

    [Fact]
    public void Accurate_cut_uses_the_profile_container()
    {
        Cut(CutMode.Accurate).OutputPath.ShouldEndWith(".mp4");
        Cut(CutMode.Accurate, new EditorProfile(EditorProfileKind.DnxhrHq)).OutputPath.ShouldEndWith(".mov");
    }

    [Theory]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("  name.  ", "name")]
    [InlineData("", "Clip")]
    [InlineData("अंतिम मिशन", "अंतिम मिशन")]
    public void Safe_file_names(string name, string expected) => FileNames.Safe(name).ShouldBe(expected);

    [Fact]
    public void Long_names_are_trimmed() => FileNames.Safe(new string('x', 300)).Length.ShouldBe(150);

    [Theory]
    [InlineData("https://www.example.com/watch?v=TestVid0001", true)]
    [InlineData(@"E:\raw\a.mp4", false)]
    public void Frame_input_kind(string input, bool isUrl) =>
        new FrameOptions { Input = input, OutputDirectory = @"E:\frames", Name = "a" }.IsUrl.ShouldBe(isUrl);

    [Fact]
    public void Every_kind_round_trips_with_its_kind()
    {
        JobOptions[] all =
        [
            new DownloadOptions { Url = "https://www.example.com/v", OutputDirectory = @"E:\raw", Sections = ["*0:01-0:02"], CutMode = CutMode.Accurate, Profile = new EditorProfile(EditorProfileKind.H264, "h264_amf") },
            Cut(CutMode.Accurate, new EditorProfile(EditorProfileKind.ProRes422)),
            new FrameOptions { Input = @"E:\raw\a.mp4", OutputDirectory = @"E:\frames", Name = "Boss", Mode = FrameMode.ContactSheet, EverySeconds = 5, Duration = TimeSpan.FromMinutes(3) },
        ];

        foreach (var options in all)
        {
            var copy = JobOptionsSerializer.Deserialize(options.Kind, JobOptionsSerializer.Serialize(options));

            copy.GetType().ShouldBe(options.GetType());
            JobOptionsSerializer.Serialize(copy).ShouldBe(JobOptionsSerializer.Serialize(options));
        }
    }

    [Fact]
    public void Stored_phase_2_downloads_still_load() =>
        JobOptionsSerializer.Deserialize(JobKind.Download, """{"url":"https://www.example.com/v","outputDirectory":"E:\\raw","preset":"audioOnly"}""")
            .ShouldBeOfType<DownloadOptions>().Preset.ShouldBe(FormatPreset.AudioOnly);

    [Fact]
    public void Wrong_json_for_a_kind_throws() =>
        Should.Throw<JsonException>(() => JobOptionsSerializer.Deserialize(JobKind.Cut, """{"outputDirectory":"E:\\raw"}"""));

    [Fact]
    public void Queue_jobs_take_their_kind_from_the_options() =>
        new QueueJob { Title = "x", Options = Cut(CutMode.Fast) }.Kind.ShouldBe(JobKind.Cut);
}
