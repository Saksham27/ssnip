using SSnip.Core;
using SSnip.Core.Clipping;
using SSnip.Core.Downloads;
using SSnip.Core.Processes;
using SSnip.Core.Queue;
using SSnip.Core.Tools;

namespace SSnip.Infrastructure.Queue;

/// <summary>Runs local cuts and frame grabs with ffmpeg.</summary>
public sealed class FfmpegJobExecutor(IFfmpegService ffmpeg, IYtDlpService ytDlp, IProcessRunner processRunner, AppPaths paths) : IJobExecutor
{
    public Task<JobOutcome> RunAsync(QueueJob job, IProgress<JobUpdate> updates, CancellationToken abort, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Options switch
        {
            LocalCutOptions cut => CutAsync(job, cut, updates, abort),
            FrameOptions frames => FramesAsync(job, frames, updates, abort),
            _ => throw new ArgumentException("Not an ffmpeg job.", nameof(job)),
        };
    }

    private async Task<JobOutcome> CutAsync(QueueJob job, LocalCutOptions cut, IProgress<JobUpdate> updates, CancellationToken abort)
    {
        if (!File.Exists(cut.InputPath))
        {
            return JobOutcome.Failed($"The source file is gone: {cut.InputPath}");
        }

        var profile = cut.Profile;
        if (cut.Mode == CutMode.Accurate && profile is not { Converts: true })
        {
            profile = new EditorProfile(EditorProfileKind.H264);
        }

        if (profile is { Kind: EditorProfileKind.H264, Encoder: null })
        {
            profile = profile with { Encoder = (await ffmpeg.DetectEncodersAsync(abort).ConfigureAwait(false)).H264Encoder };
        }

        Directory.CreateDirectory(cut.OutputDirectory);
        var output = cut.OutputPath;
        var arguments = FfmpegArgsBuilder.Cut(cut.InputPath, output, cut.Start, cut.End, cut.Mode, profile);
        return await RunAsync(job, arguments, output, cut.End - cut.Start, DownloadStage.Converting, updates, abort).ConfigureAwait(false);
    }

    private async Task<JobOutcome> FramesAsync(QueueJob job, FrameOptions frames, IProgress<JobUpdate> updates, CancellationToken abort)
    {
        var input = frames.Input;
        if (frames.IsUrl)
        {
            // Grab from the best full-quality stream, never from a proxy.
            updates.Report(new JobUpdate(JobState.Fetching, new JobProgress(DownloadStage.Starting)));
            input = await ytDlp.ResolveStreamUrlAsync(frames.Input, abort).ConfigureAwait(false);
        }
        else if (!File.Exists(input))
        {
            return JobOutcome.Failed($"The source file is gone: {input}");
        }

        var name = FileNames.Safe(frames.Name);
        Directory.CreateDirectory(frames.OutputDirectory);
        switch (frames.Mode)
        {
            case FrameMode.Single:
            {
                var output = Path.Combine(frames.OutputDirectory, $"{name} {frames.At:hh\\-mm\\-ss\\.fff}.png");
                return await RunAsync(job, FfmpegArgsBuilder.FrameGrab(input, frames.At, output), output, null, DownloadStage.Finishing, updates, abort).ConfigureAwait(false);
            }

            case FrameMode.Every:
            {
                var folder = Path.Combine(frames.OutputDirectory, name);
                Directory.CreateDirectory(folder);
                return await RunAsync(job, FfmpegArgsBuilder.FramesEvery(input, frames.EverySeconds, folder), folder, frames.Duration, DownloadStage.Converting, updates, abort).ConfigureAwait(false);
            }

            default:
            {
                var duration = frames.Duration ?? (frames.IsUrl ? null : await ffmpeg.ProbeDurationAsync(input, abort).ConfigureAwait(false));
                if (duration is null)
                {
                    return JobOutcome.Failed("A contact sheet needs the video's duration.");
                }

                var output = Path.Combine(frames.OutputDirectory, $"{name} contact sheet.jpg");
                return await RunAsync(job, FfmpegArgsBuilder.ContactSheet(input, frames.EverySeconds, duration.Value, output), output, duration, DownloadStage.Converting, updates, abort).ConfigureAwait(false);
            }
        }
    }

    private async Task<JobOutcome> RunAsync(QueueJob job, IReadOnlyList<string> arguments, string output, TimeSpan? duration, DownloadStage stage, IProgress<JobUpdate> updates, CancellationToken abort)
    {
        var command = processRunner.Describe(new ProcessRequest(ExternalTool.Ffmpeg, arguments)).CommandLine;
        using var log = JobLog.Open(paths, job);
        log.Line($"# {command}");
        updates.Report(new JobUpdate(JobState.PostProcessing, new JobProgress(stage, duration is null ? null : 0), CommandLine: command, LogPath: log.Path));

        ProcessResult result;
        try
        {
            result = await ffmpeg.RunAsync(
                arguments,
                new SynchronousProgress<FfmpegProgress>(p => updates.Report(new JobUpdate(JobState.PostProcessing, YtDlpJobExecutor.ToProgress(p, duration, stage)))),
                new SynchronousProgress<OutputLine>(log.Output),
                abort).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return JobOutcome.Failed("ffmpeg is not installed. Open Settings › Tools to download it.", command);
        }

        log.Line($"# Exit code {result.ExitCode}");
        var exists = File.Exists(output) || (Directory.Exists(output) && Directory.EnumerateFiles(output).Any());
        return result.Succeeded && exists
            ? JobOutcome.Done(output, null, command)
            : JobOutcome.Failed($"ffmpeg failed with exit code {result.ExitCode}. See the job log for details.", command);
    }
}

/// <summary>Sends each job to the executor for its kind.</summary>
public sealed class JobExecutorRouter(YtDlpJobExecutor downloads, FfmpegJobExecutor ffmpegJobs) : IJobExecutor
{
    public Task<JobOutcome> RunAsync(QueueJob job, IProgress<JobUpdate> updates, CancellationToken abort, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Kind == JobKind.Download
            ? downloads.RunAsync(job, updates, abort, stop)
            : ffmpegJobs.RunAsync(job, updates, abort, stop);
    }
}
