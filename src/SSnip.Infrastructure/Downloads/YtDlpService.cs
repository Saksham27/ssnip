using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SSnip.Core.Downloads;
using SSnip.Core.Media;
using SSnip.Core.Processes;
using SSnip.Core.Tools;

namespace SSnip.Infrastructure.Downloads;

public sealed partial class YtDlpService(IProcessRunner processRunner, ILogger<YtDlpService> logger) : IYtDlpService
{
    public async Task<MediaInfo> FetchInfoAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        // --flat-playlist keeps playlist and channel listings fast and does nothing for single videos;
        // --no-playlist makes a watch?v=…&list=… link mean the video, not the whole playlist.
        var request = new ProcessRequest(ExternalTool.YtDlp, ["-J", "--flat-playlist", "--no-playlist", "--no-warnings", "--", url.Trim()]);
        var captured = await processRunner.RunAndCaptureAsync(request, cancellationToken).ConfigureAwait(false);
        var commandLine = processRunner.Describe(request).CommandLine;

        if (!captured.Result.Succeeded || string.IsNullOrWhiteSpace(captured.StandardOutput))
        {
            throw new YtDlpException(FirstError(captured.StandardError) ?? $"yt-dlp exited with code {captured.Result.ExitCode}.", commandLine);
        }

        try
        {
            return MediaInfoParser.Parse(captured.StandardOutput);
        }
        catch (JsonException ex)
        {
            LogBadJson(ex, url);
            throw new YtDlpException("yt-dlp returned information SSnip could not read.", commandLine);
        }
    }

    public async Task<DownloadResult> DownloadAsync(
        DownloadOptions options,
        IProgress<YtDlpEvent>? events = null,
        CancellationToken cancellationToken = default,
        IProgress<OutputLine>? output = null,
        CancellationToken stop = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var request = Request(options);
        var commandLine = processRunner.Describe(request).CommandLine;
        string? finalPath = null;
        string? lastDownloadPath = null;
        var sawProgress = false;
        var errors = new List<string>();

        // Called on the process runner's reader threads; events are forwarded as they arrive.
        var lines = new SynchronousProgress<OutputLine>(line =>
        {
            output?.Report(line);
            var parsed = YtDlpOutputParser.Parse(line);
            switch (parsed)
            {
                case FinalFileEvent file:
                    finalPath = file.Path;
                    break;
                case DownloadProgressEvent { IsAlreadyDownloaded: false } progress:
                    sawProgress = true;
                    lastDownloadPath = progress.FileName ?? lastDownloadPath;
                    break;
                case ErrorEvent error:
                    lock (errors)
                    {
                        errors.Add(error.Message);
                    }

                    break;
            }

            if (parsed is not null)
            {
                events?.Report(parsed);
            }
        });

        Directory.CreateDirectory(options.OutputDirectory);
        var stopwatch = Stopwatch.StartNew();
        var result = await processRunner.RunAsync(request, lines, cancellationToken, stop).ConfigureAwait(false);

        // A date filter that rejects the video also ends with no file and no error.
        if (result.Succeeded && finalPath is null && errors.Count == 0 && !options.HasDateFilter)
        {
            errors.Add("yt-dlp finished without reporting a file.");
        }

        // An existing file is skipped quietly: yt-dlp prints its path with no real progress, at most a
        // "finished" line without an elapsed time.
        var wasSkipped = result.Succeeded && finalPath is not null && !sawProgress;
        return new DownloadResult(result.ExitCode, commandLine, finalPath, errors, stopwatch.Elapsed, wasSkipped, result.WasInterrupted, lastDownloadPath);
    }

    public string DescribeCommand(DownloadOptions options) => processRunner.Describe(Request(options)).CommandLine;

    private static ProcessRequest Request(DownloadOptions options) =>
        new(ExternalTool.YtDlp, YtDlpArgsBuilder.Build(options));

    private static string? FirstError(string stderr)
    {
        foreach (var line in stderr.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (YtDlpOutputParser.Parse(new OutputLine(OutputStream.StandardError, line)) is ErrorEvent error)
            {
                return error.Message;
            }
        }

        var rest = stderr.Trim();
        return rest.Length == 0 ? null : rest.Length > 500 ? rest[^500..] : rest;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "yt-dlp -J output for {Url} was not valid JSON")]
    private partial void LogBadJson(Exception exception, string url);
}
