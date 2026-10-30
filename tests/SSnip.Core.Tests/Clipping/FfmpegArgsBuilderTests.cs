using Shouldly;
using SSnip.Core.Clipping;

namespace SSnip.Core.Tests.Clipping;

public class FfmpegArgsBuilderTests
{
    private const string In = @"E:\Footage\raw\मॉडर्न [TestVid0001].mkv";
    private const string Out = @"E:\Footage\clips\Soap scene.mp4";

    private static string ValueOf(IReadOnlyList<string> args, string flag)
    {
        var index = args.ToList().IndexOf(flag);
        index.ShouldBeGreaterThanOrEqualTo(0, $"{flag} missing");
        return args[index + 1];
    }

    [Fact]
    public void Amd_h264_uses_constant_quality()
    {
        var args = FfmpegArgsBuilder.Convert(In, Out, new EditorProfile(EditorProfileKind.H264, "h264_amf"));

        ValueOf(args, "-i").ShouldBe(In);
        ValueOf(args, "-c:v").ShouldBe("h264_amf");
        ValueOf(args, "-rc").ShouldBe("cqp");
        ValueOf(args, "-c:a").ShouldBe("aac");
        ValueOf(args, "-ar").ShouldBe("48000");
        ValueOf(args, "-fps_mode").ShouldBe("cfr");
        args.ShouldContain("+faststart");
        args[^1].ShouldBe(Out);
    }

    [Fact]
    public void Without_a_hardware_encoder_h264_falls_back_to_libx264_crf_16()
    {
        var args = FfmpegArgsBuilder.Convert(In, Out, new EditorProfile(EditorProfileKind.H264));

        ValueOf(args, "-c:v").ShouldBe("libx264");
        ValueOf(args, "-crf").ShouldBe("16");
    }

    [Fact]
    public void Dnxhr_hq_in_mov()
    {
        var profile = new EditorProfile(EditorProfileKind.DnxhrHq);
        var args = FfmpegArgsBuilder.Convert(In, @"E:\a.mov", profile);

        ValueOf(args, "-c:v").ShouldBe("dnxhd");
        ValueOf(args, "-profile:v").ShouldBe("dnxhr_hq");
        profile.Extension.ShouldBe("mov");
        args.ShouldNotContain("+faststart");
    }

    [Fact]
    public void Prores_422()
    {
        var args = FfmpegArgsBuilder.Convert(In, @"E:\a.mov", new EditorProfile(EditorProfileKind.ProRes422));

        ValueOf(args, "-c:v").ShouldBe("prores_ks");
        ValueOf(args, "-profile:v").ShouldBe("2");
    }

    [Fact]
    public void Scale_and_fps_share_one_filter_chain() =>
        ValueOf(FfmpegArgsBuilder.Convert(In, Out, new EditorProfile(EditorProfileKind.H264, TargetFps: 30, TargetHeight: 1080)), "-vf")
            .ShouldBe("scale=-2:1080:flags=lanczos,fps=30");

    [Fact]
    public void Variable_frame_rate_can_be_kept() =>
        FfmpegArgsBuilder.Convert(In, Out, new EditorProfile(EditorProfileKind.H264, ConstantFrameRate: false)).ShouldNotContain("-fps_mode");

    [Fact]
    public void Pcm_audio_goes_into_mov()
    {
        var profile = new EditorProfile(EditorProfileKind.H264, Audio: ProfileAudio.Pcm48k);

        ValueOf(FfmpegArgsBuilder.Encode(profile), "-c:a").ShouldBe("pcm_s16le");
        profile.Extension.ShouldBe("mov");
    }

    [Fact]
    public void Cover_images_are_not_mapped() =>
        FfmpegArgsBuilder.Encode(new EditorProfile(EditorProfileKind.H264)).Take(4).ShouldBe(["-map", "0:v:0", "-map", "0:a?"]);

    [Fact]
    public void Keep_original_cannot_be_converted() =>
        Should.Throw<ArgumentException>(() => FfmpegArgsBuilder.Convert(In, Out, EditorProfile.Original));

    [Fact]
    public void Fast_local_cut_copies_streams()
    {
        var args = FfmpegArgsBuilder.Cut(In, Out, TimeSpan.FromSeconds(750.25), TimeSpan.FromSeconds(790), CutMode.Fast);

        ValueOf(args, "-ss").ShouldBe("750.250");
        ValueOf(args, "-t").ShouldBe("39.750");
        ValueOf(args, "-c").ShouldBe("copy");
        args.ToList().IndexOf("-ss").ShouldBeLessThan(args.ToList().IndexOf("-i"));
    }

    [Fact]
    public void Accurate_local_cut_re_encodes_even_when_keeping_the_original()
    {
        var args = FfmpegArgsBuilder.Cut(In, Out, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), CutMode.Accurate, EditorProfile.Original);

        ValueOf(args, "-c:v").ShouldBe("libx264");
        args.ShouldNotContain("copy");
    }

    [Fact]
    public void Accurate_local_cut_uses_the_chosen_profile() =>
        ValueOf(FfmpegArgsBuilder.Cut(In, Out, TimeSpan.Zero, TimeSpan.FromSeconds(5), CutMode.Accurate, new EditorProfile(EditorProfileKind.DnxhrHq)), "-c:v").ShouldBe("dnxhd");

    [Fact]
    public void Backwards_cut_throws() =>
        Should.Throw<ArgumentException>(() => FfmpegArgsBuilder.Cut(In, Out, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), CutMode.Fast));

    [Fact]
    public void Frame_grab_writes_one_png()
    {
        var args = FfmpegArgsBuilder.FrameGrab("https://example.com/stream.mp4", TimeSpan.FromSeconds(83.5), @"E:\frames\a.png");

        ValueOf(args, "-ss").ShouldBe("83.500");
        ValueOf(args, "-frames:v").ShouldBe("1");
        args[^1].ShouldBe(@"E:\frames\a.png");
    }

    [Fact]
    public void Frames_every_n_seconds() =>
        ValueOf(FfmpegArgsBuilder.FramesEvery(In, 2.5, @"E:\frames"), "-vf").ShouldBe("fps=1/2.5");

    [Fact]
    public void Contact_sheet_tiles_enough_rows() =>
        ValueOf(FfmpegArgsBuilder.ContactSheet(In, 10, TimeSpan.FromSeconds(125), @"E:\frames\sheet.jpg", columns: 6), "-vf")
            .ShouldBe("fps=1/10,scale=320:-2,tile=6x3:padding=4:margin=4");

    [Fact]
    public void Every_command_reports_progress()
    {
        foreach (var args in new[]
        {
            FfmpegArgsBuilder.Convert(In, Out, new EditorProfile(EditorProfileKind.H264)),
            FfmpegArgsBuilder.Cut(In, Out, TimeSpan.Zero, TimeSpan.FromSeconds(1), CutMode.Fast),
            FfmpegArgsBuilder.FrameGrab(In, TimeSpan.Zero, "a.png"),
        })
        {
            ValueOf(args, "-progress").ShouldBe("pipe:1");
            args.ShouldContain("-nostdin");
        }
    }

    [Fact]
    public void Test_encode_uses_a_generated_source() =>
        ValueOf(FfmpegArgsBuilder.TestEncoder("h264_amf"), "-c:v").ShouldBe("h264_amf");
}
