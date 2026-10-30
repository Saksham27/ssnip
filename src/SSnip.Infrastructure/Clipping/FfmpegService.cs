using System.Globalization;
using Microsoft.Extensions.Logging;
using SSnip.Core.Clipping;
using SSnip.Core.Processes;
using SSnip.Core.Tools;

namespace SSnip.Infrastructure.Clipping;

public sealed partial class FfmpegService(IProcessRunner processRunner, ILogger<FfmpegService> logger) : IFfmpegService
{
    private readonly SemaphoreSlim _detectGate = new(1, 1);
    private EncoderSupport? _encoders;

    public async Task<EncoderSupport> DetectEncodersAsync(CancellationToken cancellationToken = default)
    {
        if (_encoders is not null)
        {
            return _encoders;
        }

        await _detectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_encoders is not null)
            {
                return _encoders;
            }

            var list = await processRunner.RunAndCaptureAsync(new ProcessRequest(ExternalTool.Ffmpeg, ["-hide_banner", "-encoders"]), cancellationToken).ConfigureAwait(false);
            var available = EncoderList.Parse(list.StandardOutput);

            var working = new List<string>();
            foreach (var encoder in FfmpegArgsBuilder.HardwareH264Encoders.Where(available.Contains))
            {
                var test = await processRunner.RunAndCaptureAsync(new ProcessRequest(ExternalTool.Ffmpeg, FfmpegArgsBuilder.TestEncoder(encoder)), cancellationToken).ConfigureAwait(false);
                if (test.Result.Succeeded)
                {
                    working.Add(encoder);
                }
            }

            _encoders = new EncoderSupport(working.FirstOrDefault() ?? FfmpegArgsBuilder.SoftwareH264Encoder, available, working);
            LogEncoders(_encoders.H264Encoder, string.Join(", ", working));
            return _encoders;
        }
        finally
        {
            _detectGate.Release();
        }
    }

    public Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        IProgress<FfmpegProgress>? progress = null,
        IProgress<OutputLine>? output = null,
        CancellationToken cancellationToken = default)
    {
        var parser = new FfmpegProgressParser();
        var lines = new SynchronousProgress<OutputLine>(line =>
        {
            output?.Report(line);
            if (line.Stream == OutputStream.StandardOutput && parser.Read(line.Text) is { } p)
            {
                progress?.Report(p);
            }
        });

        return processRunner.RunAsync(new ProcessRequest(ExternalTool.Ffmpeg, arguments), lines, cancellationToken);
    }

    public async Task<TimeSpan?> ProbeDurationAsync(string path, CancellationToken cancellationToken = default)
    {
        var probe = await processRunner.RunAndCaptureAsync(
            new ProcessRequest(ExternalTool.Ffprobe, ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path]),
            cancellationToken).ConfigureAwait(false);

        return probe.Result.Succeeded && double.TryParse(probe.StandardOutput.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "H.264 encoder: {Encoder}; working hardware encoders: {Hardware}")]
    private partial void LogEncoders(string encoder, string hardware);
}
