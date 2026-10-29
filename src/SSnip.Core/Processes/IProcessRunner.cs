using System.Text;

namespace SSnip.Core.Processes;

/// <summary>
/// The one place that starts yt-dlp, ffmpeg, ffprobe or Deno. Output streams line by line;
/// cancelling kills the whole process tree and throws <see cref="OperationCanceledException"/>.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<OutputLine>? output = null, CancellationToken cancellationToken = default);

    /// <summary>The exact executable, arguments and command line <see cref="RunAsync"/> would use.</summary>
    ProcessLaunchPlan Describe(ProcessRequest request);
}

public static class ProcessRunnerExtensions
{
    /// <summary>Runs a short command and collects its output. For long jobs, stream with <see cref="IProcessRunner.RunAsync"/>.</summary>
    public static async Task<CapturedProcessResult> RunAndCaptureAsync(
        this IProcessRunner runner,
        ProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runner);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var collector = new SynchronousProgress<OutputLine>(line =>
        {
            var target = line.Stream == OutputStream.StandardOutput ? stdout : stderr;
            lock (target)
            {
                target.AppendLine(line.Text);
            }
        });

        var result = await runner.RunAsync(request, collector, cancellationToken).ConfigureAwait(false);
        return new CapturedProcessResult(result, stdout.ToString(), stderr.ToString());
    }
}

public sealed record CapturedProcessResult(ProcessResult Result, string StandardOutput, string StandardError);

/// <summary>An <see cref="IProgress{T}"/> that calls back on the reporting thread, unlike <see cref="Progress{T}"/>.</summary>
public sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
