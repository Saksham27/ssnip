using System.Text.RegularExpressions;
using SSnip.Core.Media;

namespace SSnip.Core.Downloads;

/// <summary>Job states from the plan.</summary>
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

    /// <summary>A live stream is being recorded.</summary>
    Recording,
}

public static partial class DownloadStageResolver
{
    /// <summary>
    /// The stage for a progress line: the codecs of the format being downloaded say whether it is the
    /// video or the audio part; without them, the file name decides.
    /// </summary>
    public static DownloadStage FromProgress(DownloadProgressEvent progress, MediaInfo? info = null)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return (progress.VideoCodec, progress.AudioCodec) switch
        {
            (not null, null) => DownloadStage.Video,
            (null, not null) => DownloadStage.Audio,
            (not null, not null) => DownloadStage.Downloading,
            _ => FromFileName(progress.FileName, info),
        };
    }

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
        DownloadStage.Recording => "Recording",
        _ => stage.ToString(),
    };

    private static bool IsAudioExtension(string ext) => ext is "m4a" or "mp3" or "opus" or "ogg" or "aac" or "wav" or "flac" or "weba";

    [GeneratedRegex(@"\.f(?<id>[0-9A-Za-z_-]+)\.(?<ext>[0-9A-Za-z]+)(\.part)?$")]
    private static partial Regex FormatSuffix();
}
