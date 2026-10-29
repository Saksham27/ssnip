using System.Text.RegularExpressions;
using SSnip.Core.Media;

namespace SSnip.Core.Downloads;

/// <summary>Job states from the plan. Phase 1 runs one job at a time; the queue arrives in Phase 2.</summary>
public enum JobState
{
    Queued,
    Fetching,
    Downloading,
    PostProcessing,
    Done,
    Failed,
    Cancelled,
    Paused,
}

/// <summary>What a running download is doing right now, shown next to its progress.</summary>
public enum DownloadStage
{
    Starting,
    Downloading,
    Video,
    Audio,
    Merging,
    ExtractingAudio,
    Converting,
    PostProcessing,
    Finishing,
}

public static partial class DownloadStageResolver
{
    /// <summary>
    /// The stage for a progress line. Split video + audio downloads name each part with a format
    /// suffix (<c>Title [id].f137.mp4</c>), which tells which stream is downloading.
    /// </summary>
    public static DownloadStage FromFileName(string? fileName, MediaInfo? info)
    {
        if (fileName is null)
        {
            return DownloadStage.Downloading;
        }

        var match = FormatSuffix().Match(fileName);
        if (!match.Success)
        {
            return DownloadStage.Downloading;
        }

        var format = info?.FindFormat(match.Groups["id"].Value);
        return format switch
        {
            { IsVideoOnly: true } => DownloadStage.Video,
            { IsAudioOnly: true } => DownloadStage.Audio,
            null when IsAudioExtension(match.Groups["ext"].Value) => DownloadStage.Audio,
            _ => DownloadStage.Downloading,
        };
    }

    public static DownloadStage FromPostProcessor(string name) => name switch
    {
        "Merger" => DownloadStage.Merging,
        "ExtractAudio" or "FFmpegExtractAudio" => DownloadStage.ExtractingAudio,
        "VideoConvertor" or "VideoRemuxer" or "FFmpegVideoConvertor" or "FFmpegVideoRemuxer" => DownloadStage.Converting,
        "MoveFiles" or "MoveFilesAfterDownload" => DownloadStage.Finishing,
        _ => DownloadStage.PostProcessing,
    };

    public static string Describe(DownloadStage stage) => stage switch
    {
        DownloadStage.Starting => "Starting",
        DownloadStage.Downloading => "Downloading",
        DownloadStage.Video => "Downloading video",
        DownloadStage.Audio => "Downloading audio",
        DownloadStage.Merging => "Merging video and audio",
        DownloadStage.ExtractingAudio => "Extracting audio",
        DownloadStage.Converting => "Converting",
        DownloadStage.PostProcessing => "Post-processing",
        DownloadStage.Finishing => "Finishing",
        _ => stage.ToString(),
    };

    private static bool IsAudioExtension(string ext) => ext is "m4a" or "mp3" or "opus" or "ogg" or "aac" or "wav" or "flac" or "weba";

    [GeneratedRegex(@"\.f(?<id>[0-9A-Za-z_-]+)\.(?<ext>[0-9A-Za-z]+)(\.part)?$")]
    private static partial Regex FormatSuffix();
}
