using System.Globalization;
using System.Text;
using SSnip.Core;
using SSnip.Core.Downloads;
using SSnip.Core.Processes;
using SSnip.Core.Queue;

namespace SSnip.Infrastructure.Queue;

/// <summary>
/// One job's log in <c>logs\jobs\job-&lt;id&gt;.log</c>: the command lines and all output. Output arrives
/// on two reader threads. Progress lines (yt-dlp's FSPROG and ffmpeg's <c>key=value</c> blocks) are
/// kept at most once a second, so an hour-long recording does not write a line per fragment.
/// </summary>
internal sealed class JobLog : IDisposable
{
    private readonly StreamWriter _writer;
    private long _lastProgressTicks;
    private bool _disposed;

    private JobLog(string path) => _writer = new StreamWriter(path, append: true, new UTF8Encoding(false)) { AutoFlush = true };

    public string Path { get; private init; } = "";

    /// <summary>Opens the job's log and writes its header.</summary>
    public static JobLog Open(AppPaths paths, QueueJob job)
    {
        Directory.CreateDirectory(paths.JobLogs);
        var path = paths.JobLogFile(job.Id);
        var log = new JobLog(path) { Path = path };
        log.Line($"# {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} job {job.Id} ({job.Kind}){(job.ResumePartial ? " resuming" : "")}");
        return log;
    }

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
        if (IsFfmpegProgressDetail(line))
        {
            return;
        }

        if (IsProgress(line))
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

    private static bool IsProgress(OutputLine line) =>
        (line.Text.StartsWith(YtDlpArgsBuilder.ProgressPrefix, StringComparison.Ordinal) && !line.Text.Contains("\"finished\"", StringComparison.Ordinal))
        || (line.Stream == OutputStream.StandardOutput && line.Text.StartsWith("out_time=", StringComparison.Ordinal));

    /// <summary>ffmpeg's <c>-progress pipe:1</c> blocks: only <c>out_time</c> and <c>progress=end</c> are kept.</summary>
    private static bool IsFfmpegProgressDetail(OutputLine line)
    {
        var text = line.Text;
        var equals = text.IndexOf('=', StringComparison.Ordinal);
        return line.Stream == OutputStream.StandardOutput && equals > 0 && !text.Contains(' ', StringComparison.Ordinal)
            && !text.StartsWith(YtDlpArgsBuilder.ProgressPrefix, StringComparison.Ordinal)
            && !text.StartsWith(YtDlpArgsBuilder.FilePrefix, StringComparison.Ordinal)
            && text != "progress=end" && !text.StartsWith("out_time=", StringComparison.Ordinal);
    }
}
