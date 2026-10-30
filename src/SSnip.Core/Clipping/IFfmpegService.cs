using SSnip.Core.Processes;

namespace SSnip.Core.Clipping;

/// <summary>Which encoders this PC's ffmpeg can actually use.</summary>
/// <param name="H264Encoder">The best working H.264 encoder: a hardware one when it test-encodes, else <c>libx264</c>.</param>
/// <param name="Available">Every encoder ffmpeg was built with, including hardware ones that may not work here.</param>
/// <param name="WorkingHardware">Hardware encoders that passed a one-second test encode.</param>
public sealed record EncoderSupport(string H264Encoder, IReadOnlySet<string> Available, IReadOnlyList<string> WorkingHardware)
{
    public bool HasDnxhr => Available.Contains("dnxhd");

    public bool HasProRes => Available.Contains("prores_ks");
}

/// <summary>Runs ffmpeg and ffprobe through the process runner.</summary>
public interface IFfmpegService
{
    /// <summary>
    /// Lists encoders with <c>ffmpeg -encoders</c>, then test-encodes each hardware H.264 encoder,
    /// because the list also names encoders for GPUs this PC does not have. Cached after the first call.
    /// </summary>
    Task<EncoderSupport> DetectEncodersAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs ffmpeg with <paramref name="arguments"/>, reporting progress against <paramref name="duration"/>.</summary>
    Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        IProgress<FfmpegProgress>? progress = null,
        IProgress<OutputLine>? output = null,
        CancellationToken cancellationToken = default);

    /// <summary>The container duration from ffprobe, or null when it cannot be read.</summary>
    Task<TimeSpan?> ProbeDurationAsync(string path, CancellationToken cancellationToken = default);
}
