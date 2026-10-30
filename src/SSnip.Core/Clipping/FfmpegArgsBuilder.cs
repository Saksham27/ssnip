using System.Globalization;

namespace SSnip.Core.Clipping;

/// <summary>
/// Pure functions from clipping choices to ffmpeg arguments. Every command reports progress on
/// stdout with <c>-progress pipe:1</c>, read by <see cref="FfmpegProgressParser"/>.
/// </summary>
public static class FfmpegArgsBuilder
{
    /// <summary>H.264 hardware encoders in the order SSnip prefers them.</summary>
    public static IReadOnlyList<string> HardwareH264Encoders { get; } = ["h264_amf", "h264_nvenc", "h264_qsv"];

    public const string SoftwareH264Encoder = "libx264";

    /// <summary>Converts a whole file with an editor profile.</summary>
    public static IReadOnlyList<string> Convert(string input, string output, EditorProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.Converts)
        {
            throw new ArgumentException("Keep original needs no conversion.", nameof(profile));
        }

        return [.. Start(), "-i", input, .. Encode(profile), .. End(output)];
    }

    /// <summary>
    /// Cuts <paramref name="start"/>–<paramref name="end"/> out of a local file. Fast copies the
    /// streams (cuts land on keyframes); Accurate re-encodes with <paramref name="profile"/>, or H.264 when
    /// the profile keeps the original, so the cut lands on the exact frame.
    /// </summary>
    public static IReadOnlyList<string> Cut(string input, string output, TimeSpan start, TimeSpan end, CutMode mode, EditorProfile? profile = null)
    {
        if (end <= start || start < TimeSpan.Zero)
        {
            throw new ArgumentException("The cut must end after it starts.", nameof(end));
        }

        // Seeking before -i is fast, and exact once the frames are decoded and re-encoded.
        List<string> args = [.. Start(), "-ss", ClipTime.Seconds(start), "-i", input, "-t", ClipTime.Seconds(end - start)];
        if (mode == CutMode.Fast)
        {
            args.AddRange(["-map", "0:v:0?", "-map", "0:a?", "-c", "copy", "-avoid_negative_ts", "make_zero"]);
        }
        else
        {
            args.AddRange(Encode(profile is { Converts: true } ? profile : new EditorProfile(EditorProfileKind.H264)));
        }

        return [.. args, .. End(output)];
    }

    /// <summary>One full-resolution PNG of the frame at <paramref name="time"/>, from a file or a stream URL.</summary>
    public static IReadOnlyList<string> FrameGrab(string input, TimeSpan time, string outputPng) =>
        [.. Start(), "-ss", ClipTime.Seconds(time), "-i", input, "-map", "0:v:0", "-frames:v", "1", "-update", "1", .. End(outputPng)];

    /// <summary>One PNG every <paramref name="everySeconds"/> seconds, numbered: <c>frame_00001.png</c>…</summary>
    public static IReadOnlyList<string> FramesEvery(string input, double everySeconds, string outputFolder)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(everySeconds);
        return [.. Start(), "-i", input, "-map", "0:v:0", "-vf", $"fps=1/{Number(everySeconds)}", .. End(Path.Combine(outputFolder, "frame_%05d.png"))];
    }

    /// <summary>
    /// A storyboard image: one thumbnail every <paramref name="everySeconds"/> seconds, 320 px wide,
    /// laid out <paramref name="columns"/> across.
    /// </summary>
    public static IReadOnlyList<string> ContactSheet(string input, double everySeconds, TimeSpan duration, string outputJpg, int columns = 6)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(everySeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        var count = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds / everySeconds));
        var rows = (int)Math.Ceiling(count / (double)columns);
        return
        [
            .. Start(), "-i", input, "-map", "0:v:0",
            "-vf", $"fps=1/{Number(everySeconds)},scale=320:-2,tile={columns}x{rows}:padding=4:margin=4",
            "-frames:v", "1", "-q:v", "3",
            .. End(outputJpg),
        ];
    }

    /// <summary>A one-second test encode that tells whether a hardware encoder really works on this PC.</summary>
    public static IReadOnlyList<string> TestEncoder(string encoder) =>
        ["-hide_banner", "-nostdin", "-v", "error", "-f", "lavfi", "-i", "testsrc2=duration=1:size=1280x720:rate=30", "-c:v", encoder, "-f", "null", "-"];

    /// <summary>Video and audio encoding arguments for a profile.</summary>
    public static IReadOnlyList<string> Encode(EditorProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // The first video stream and any audio; embedded cover images are left out.
        List<string> args = ["-map", "0:v:0", "-map", "0:a?"];

        var filters = new List<string>();
        if (profile.TargetHeight is { } height)
        {
            filters.Add($"scale=-2:{height.ToString(CultureInfo.InvariantCulture)}:flags=lanczos");
        }

        if (profile.TargetFps is { } fps)
        {
            filters.Add($"fps={fps.ToString(CultureInfo.InvariantCulture)}");
        }

        if (filters.Count > 0)
        {
            args.AddRange(["-vf", string.Join(",", filters)]);
        }

        if (profile.ConstantFrameRate)
        {
            args.AddRange(["-fps_mode", "cfr"]);
        }

        args.AddRange(profile.Kind switch
        {
            EditorProfileKind.H264 => H264(profile.Encoder),
            EditorProfileKind.DnxhrHq => ["-c:v", "dnxhd", "-profile:v", "dnxhr_hq", "-pix_fmt", "yuv422p"],
            EditorProfileKind.ProRes422 => ["-c:v", "prores_ks", "-profile:v", "2", "-pix_fmt", "yuv422p10le", "-vendor", "apl0"],
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile.Kind, "Keep original has no encoder."),
        });

        args.AddRange(profile.Audio == ProfileAudio.Pcm48k
            ? ["-c:a", "pcm_s16le", "-ar", "48000"]
            : ["-c:a", "aac", "-b:a", "320k", "-ar", "48000"]);

        if (profile.Extension == "mp4")
        {
            args.AddRange(["-movflags", "+faststart"]);
        }

        return args;
    }

    private static IReadOnlyList<string> H264(string? encoder) => encoder switch
    {
        // Constant quality close to visually lossless; AMF ignores -crf.
        "h264_amf" => ["-c:v", "h264_amf", "-quality", "quality", "-rc", "cqp", "-qp_i", "16", "-qp_p", "18", "-qp_b", "20", "-pix_fmt", "yuv420p"],
        "h264_nvenc" => ["-c:v", "h264_nvenc", "-preset", "p6", "-rc", "vbr", "-cq", "16", "-b:v", "0", "-pix_fmt", "yuv420p"],
        "h264_qsv" => ["-c:v", "h264_qsv", "-preset", "slow", "-global_quality", "18", "-pix_fmt", "nv12"],
        _ => ["-c:v", SoftwareH264Encoder, "-preset", "slow", "-crf", "16", "-pix_fmt", "yuv420p"],
    };

    private static IReadOnlyList<string> Start() => ["-hide_banner", "-nostdin", "-y"];

    private static IReadOnlyList<string> End(string output) => ["-progress", "pipe:1", "-nostats", output];

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
