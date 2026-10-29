using SSnip.Core.Media;

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
    Task<DownloadResult> DownloadAsync(DownloadOptions options, IProgress<YtDlpEvent>? events = null, CancellationToken cancellationToken = default);

    /// <summary>The exact command line a download will run, for "Show command".</summary>
    string DescribeCommand(DownloadOptions options);
}

/// <param name="WasSkipped">
/// The file already existed and nothing was downloaded: yt-dlp printed the final path without any
/// progress. Only possible without <see cref="DownloadOptions.Overwrite"/>.
/// </param>
public sealed record DownloadResult(int ExitCode, string CommandLine, string? FilePath, IReadOnlyList<string> Errors, TimeSpan Duration, bool WasSkipped = false)
{
    public bool Succeeded => ExitCode == 0 && FilePath is not null;
}

public sealed class YtDlpException(string message, string? commandLine = null) : Exception(message)
{
    public string? CommandLine { get; } = commandLine;
}
