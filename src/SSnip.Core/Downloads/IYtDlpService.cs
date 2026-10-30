using SSnip.Core.Media;
using SSnip.Core.Processes;

namespace SSnip.Core.Downloads;

/// <summary>Fetches info and runs downloads through the process runner.</summary>
public interface IYtDlpService
{
    /// <summary>Runs <c>yt-dlp -J --flat-playlist</c>. Throws <see cref="YtDlpException"/> when yt-dlp fails.</summary>
    Task<MediaInfo> FetchInfoAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a download, reporting each parsed output line. Cancelling kills yt-dlp and its ffmpeg
    /// children and throws <see cref="OperationCanceledException"/>; <c>.part</c> files are kept.
    /// </summary>
    /// <param name="output">Every raw output line, for the job log.</param>
    /// <param name="stop">Ends a live recording with Ctrl+C so yt-dlp finishes the file; see <see cref="DownloadResult.WasStopped"/>.</param>
    Task<DownloadResult> DownloadAsync(
        DownloadOptions options,
        IProgress<YtDlpEvent>? events = null,
        CancellationToken cancellationToken = default,
        IProgress<OutputLine>? output = null,
        CancellationToken stop = default);

    /// <summary>The exact command line a download will run, for "Show command".</summary>
    string DescribeCommand(DownloadOptions options);
}

/// <param name="WasSkipped">
/// The file already existed and nothing was downloaded: yt-dlp printed the final path without any
/// progress. Only possible without <see cref="DownloadOptions.Overwrite"/>.
/// </param>
/// <param name="WasStopped">A live recording was stopped on request; <see cref="FilePath"/> may still be set.</param>
/// <param name="LastDownloadPath">The file the last progress line named, for finishing a stopped recording.</param>
public sealed record DownloadResult(
    int ExitCode,
    string CommandLine,
    string? FilePath,
    IReadOnlyList<string> Errors,
    TimeSpan Duration,
    bool WasSkipped = false,
    bool WasStopped = false,
    string? LastDownloadPath = null)
{
    public bool Succeeded => ExitCode == 0 && FilePath is not null;
}

public sealed class YtDlpException(string message, string? commandLine = null) : Exception(message)
{
    public string? CommandLine { get; } = commandLine;
}
