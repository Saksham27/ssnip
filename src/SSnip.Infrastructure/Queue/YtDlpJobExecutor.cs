using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using SSnip.Core;
using SSnip.Core.Downloads;
using SSnip.Core.Processes;
using SSnip.Core.Queue;
using SSnip.Core.Tools;

namespace SSnip.Infrastructure.Queue;

/// <summary>
/// Runs a queue job with yt-dlp. Every job writes its own log (command line and all output) to
/// <c>logs\jobs\job-&lt;id&gt;.log</c>.
/// </summary>
public sealed partial class YtDlpJobExecutor(IYtDlpService ytDlp, IProcessRunner processRunner, AppPaths paths, ILogger<YtDlpJobExecutor> logger) : IJobExecutor
{
    public const string AlreadyDownloadedNote = "Already in the folder, so nothing was downloaded. Turn on Overwrite to download it again.";

    public const string OutsideDateRangeNote = "Skipped: uploaded outside the chosen date range.";

    public const string RecordingStoppedNote = "Recording stopped.";

    public async Task<JobOutcome> RunAsync(QueueJob job, IProgress<JobUpdate> updates, CancellationToken abort, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(updates);

        // An interrupted run left .part files; overwriting would throw them away.
        var options = job.ResumePartial ? job.Options with { Overwrite = false } : job.Options;
        var command = ytDlp.DescribeCommand(options);
        var logPath = paths.JobLogFile(job.Id);
        Directory.CreateDirectory(paths.JobLogs);

        using var log = new JobLog(logPath);
        log.Line($"# {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} job {job.Id}{(job.ResumePartial ? " (resuming)" : "")}");
        log.Line($"# {command}");
        updates.Report(new JobUpdate(CommandLine: command, LogPath: logPath));

        var events = new SynchronousProgress<YtDlpEvent>(e =>
        {
            switch (e)
            {
                case DownloadProgressEvent progress:
                    updates.Report(new JobUpdate(JobState.Downloading, ToProgress(progress)));
                    break;
                // SponsorBlock only looks up segments before the download; it is not post-processing.
                case PostProcessEvent { IsStarted: true, PostProcessor: not "SponsorBlock" } postProcess:
                    updates.Report(new JobUpdate(JobState.PostProcessing, new JobProgress(DownloadStageResolver.FromPostProcessor(postProcess.PostProcessor))));
                    break;
                case TitleEvent title:
                    updates.Report(new JobUpdate(Title: title.Title));
                    break;
            }
        });

        // yt-dlp hands live streams to ffmpeg, which reports no progress, so show the growing file.
        using var recording = options.Live is null ? null : WatchRecording(options.OutputDirectory, updates);

        DownloadResult result;
        try
        {
            result = await ytDlp.DownloadAsync(options, events, abort, new SynchronousProgress<OutputLine>(log.Output), stop).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            log.Line("# Stopped: paused, cancelled or the app closed. Partial files are kept.");
            throw;
        }
        catch (FileNotFoundException)
        {
            log.Line("# yt-dlp is not installed.");
            return JobOutcome.Failed("yt-dlp is not installed. Open Settings › Tools to download it.", command);
        }

        log.Line($"# Exit code {result.ExitCode} after {result.Duration:hh\\:mm\\:ss}");

        if (result.WasStopped)
        {
            return await FinishRecordingAsync(result, log, abort).ConfigureAwait(false) is { } recorded
                ? JobOutcome.Done(recorded, RecordingStoppedNote, command)
                : JobOutcome.Failed("The recording stopped before anything was saved.", command);
        }

        if (result.Succeeded)
        {
            return JobOutcome.Done(result.FilePath, result.WasSkipped ? AlreadyDownloadedNote : null, command);
        }

        if (result.ExitCode == 0 && result.FilePath is null && options.HasDateFilter)
        {
            return JobOutcome.Done(null, OutsideDateRangeNote, command);
        }

        return JobOutcome.Failed(result.Errors.LastOrDefault() ?? $"yt-dlp exited with code {result.ExitCode}.", command);
    }

    /// <summary>Every two seconds, reports the size of the newest partial file in the folder as recording progress.</summary>
    private static Timer WatchRecording(string folder, IProgress<JobUpdate> updates)
    {
        var started = DateTime.UtcNow;
        return new Timer(
            _ =>
            {
                try
                {
                    // Windows does not update a directory's size or write time for a file that is still
                    // open, so pick it by creation time and read its length from an open handle.
                    var partial = new DirectoryInfo(folder).EnumerateFiles("*.part")
                        .Where(f => f.CreationTimeUtc >= started)
                        .MaxBy(f => f.CreationTimeUtc);
                    long size = 0;
                    if (partial is not null)
                    {
                        using var stream = new FileStream(partial.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        size = stream.Length;
                    }

                    updates.Report(new JobUpdate(JobState.Downloading, new JobProgress(DownloadStage.Recording, DownloadedBytes: size)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The file is being renamed; the next tick catches up.
                }
            },
            null,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2));
    }

    private static JobProgress ToProgress(DownloadProgressEvent p) => new(
        DownloadStageResolver.FromProgress(p),
        p.Fraction,
        p.DownloadedBytes,
        p.TotalBytes,
        p.IsTotalEstimate,
        p.IsFinished ? null : p.BytesPerSecond,
        p.IsFinished ? null : p.Eta,
        p.FragmentIndex,
        p.FragmentCount);

    /// <summary>
    /// After Ctrl+C, yt-dlp normally finishes and moves a live recording itself. If it only left the
    /// partial file (MPEG-TS for live streams, so it plays), remux that into an MP4 next to it.
    /// </summary>
    private async Task<string?> FinishRecordingAsync(DownloadResult result, JobLog log, CancellationToken abort)
    {
        if (result.FilePath is not null && File.Exists(result.FilePath))
        {
            return result.FilePath;
        }

        var target = result.LastDownloadPath;
        var partial = target is null ? null : new[] { target + ".part", target }.FirstOrDefault(File.Exists);
        if (target is null || partial is null || new FileInfo(partial).Length == 0)
        {
            return null;
        }

        var output = Path.ChangeExtension(target, ".mp4");
        if (string.Equals(output, partial, StringComparison.OrdinalIgnoreCase) || File.Exists(output))
        {
            output = Path.Combine(Path.GetDirectoryName(target)!, $"{Path.GetFileNameWithoutExtension(target)} (recording {DateTime.Now:HHmmss}).mp4");
        }

        log.Line($"# Remuxing the partial recording {partial} into {output}");
        var remux = await processRunner.RunAsync(
            new ProcessRequest(ExternalTool.Ffmpeg, ["-hide_banner", "-nostdin", "-y", "-i", partial, "-map", "0", "-c", "copy", "-movflags", "+faststart", output]),
            new SynchronousProgress<OutputLine>(log.Output),
            abort).ConfigureAwait(false);

        if (remux.Succeeded && File.Exists(output))
        {
            File.Delete(partial);
            return output;
        }

        LogRemuxFailed(partial, remux.ExitCode);
        return File.Exists(partial) ? partial : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Remuxing the stopped recording {Partial} failed with exit code {ExitCode}; keeping the partial file")]
    private partial void LogRemuxFailed(string partial, int exitCode);

    /// <summary>
    /// Appends to one job's log; output arrives on two reader threads. Progress lines are kept at
    /// most once a second, so an hour-long recording does not write a line per fragment.
    /// </summary>
    private sealed class JobLog(string path) : IDisposable
    {
        private readonly StreamWriter _writer = new(path, append: true, new UTF8Encoding(false)) { AutoFlush = true };
        private long _lastProgressTicks;
        private bool _disposed;

        public void Line(string text)
        {
            lock (_writer)
            {
                if (!_disposed)
                {
                    _writer.WriteLine(text);
                }
            }
        }

        public void Output(OutputLine line)
        {
            if (line.Text.StartsWith(YtDlpArgsBuilder.ProgressPrefix, StringComparison.Ordinal) && !line.Text.Contains("\"finished\"", StringComparison.Ordinal))
            {
                var now = Environment.TickCount64;
                if (now - Interlocked.Read(ref _lastProgressTicks) < 1000)
                {
                    return;
                }

                Interlocked.Exchange(ref _lastProgressTicks, now);
            }

            Line(string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:HH:mm:ss.fff} {(line.Stream == OutputStream.StandardError ? "ERR" : "OUT")} {line.Text}"));
        }

        public void Dispose()
        {
            lock (_writer)
            {
                _disposed = true;
                _writer.Dispose();
            }
        }
    }
}
