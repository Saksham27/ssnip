namespace SSnip.Core.Downloads;

/// <summary>The quality presets from the plan's Format selection table.</summary>
public enum FormatPreset
{
    /// <summary>H.264 MP4, usually up to 1080p; imports into Resolve as is.</summary>
    EditorSafe,

    /// <summary>Best video + best audio, often VP9 or AV1, in MKV.</summary>
    MaxQuality,

    /// <summary>Best up to 1080p.</summary>
    Cap1080,

    AudioOnly,

    /// <summary>Fast low-res copy for marking clips.</summary>
    PreviewProxy,

    /// <summary>Exact formats or a sort string chosen in Advanced mode.</summary>
    Custom,
}

public enum AudioFormat
{
    Wav,
    Mp3,
    M4a,
    Opus,
    Flac,
}

/// <summary>Advanced-mode choice: one video and/or one audio format id, or a <c>-S</c> sort string.</summary>
public sealed record FormatSelection(string? VideoFormatId, string? AudioFormatId, string? SortString = null)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(VideoFormatId) && string.IsNullOrWhiteSpace(AudioFormatId) && string.IsNullOrWhiteSpace(SortString);
}

/// <summary>Everything that decides one download. <see cref="YtDlpArgsBuilder"/> turns it into arguments.</summary>
public sealed record DownloadOptions
{
    public required string Url { get; init; }

    public required string OutputDirectory { get; init; }

    public FormatPreset Preset { get; init; } = FormatPreset.EditorSafe;

    /// <summary>Required when <see cref="Preset"/> is <see cref="FormatPreset.Custom"/>.</summary>
    public FormatSelection? Custom { get; init; }

    public string FilenameTemplate { get; init; } = FilenameTemplates.Default;

    /// <summary>Container for merged video + audio (mp4, mkv, mov, webm); null keeps the preset's choice.</summary>
    public string? MergeContainer { get; init; }

    public AudioFormat AudioFormat { get; init; } = AudioFormat.Wav;

    public bool Overwrite { get; init; }

    /// <summary>yt-dlp rate, for example <c>5M</c> or <c>500K</c>; null for unlimited.</summary>
    public string? RateLimit { get; init; }

    public int ConcurrentFragments { get; init; } = 4;

    public int Retries { get; init; } = 10;

    public int TrimFilenames { get; init; } = 150;

    /// <summary>Time ranges such as <c>*00:12:30-00:13:10</c>; each becomes its own <c>--download-sections</c>.</summary>
    public IReadOnlyList<string> Sections { get; init; } = [];

    /// <summary>Subtitles to save as SRT and/or embed; null for none.</summary>
    public SubtitleOptions? Subtitles { get; init; }

    public bool EmbedMetadata { get; init; }

    /// <summary>Skipped for WAV, which cannot hold a thumbnail.</summary>
    public bool EmbedThumbnail { get; init; }

    public bool EmbedChapters { get; init; }

    /// <summary>Folder that receives <c>info.json</c> (the app's data folder, not next to the video); null to skip it.</summary>
    public string? InfoJsonDirectory { get; init; }

    /// <summary>Also save the description and up to this many comments into the info folder; null to skip.</summary>
    public int? MaxComments { get; init; }

    public SponsorBlockOptions? SponsorBlock { get; init; }

    /// <summary>Skip the video unless it was uploaded on or after this date (<c>--dateafter</c>).</summary>
    public DateOnly? DateAfter { get; init; }

    /// <summary>Skip the video unless it was uploaded on or before this date (<c>--datebefore</c>).</summary>
    public DateOnly? DateBefore { get; init; }

    /// <summary>Live streams and scheduled streams; null for normal videos.</summary>
    public LiveOptions? Live { get; init; }

    /// <summary>For <see cref="Sections"/>: Accurate adds <c>--force-keyframes-at-cuts</c>, which re-encodes so cuts land on the marks.</summary>
    public Clipping.CutMode CutMode { get; init; } = Clipping.CutMode.Fast;

    /// <summary>Also write each chapter as its own file (<c>--split-chapters</c>).</summary>
    public bool SplitChapters { get; init; }

    /// <summary>Conversion run with ffmpeg after the download; not a yt-dlp option. Null keeps the file as downloaded.</summary>
    public Clipping.EditorProfile? Profile { get; init; }

    public bool HasDateFilter => DateAfter is not null || DateBefore is not null;
}

public enum SubtitleMode
{
    /// <summary>Separate <c>.srt</c> files next to the video.</summary>
    Srt,

    /// <summary>Embedded into the video file only.</summary>
    Embed,

    SrtAndEmbed,
}

/// <param name="Languages">yt-dlp language codes, for example <c>en</c> and <c>hi</c>.</param>
/// <param name="IncludeAutomatic">Also take auto-generated captions.</param>
public sealed record SubtitleOptions(IReadOnlyList<string> Languages, bool IncludeAutomatic = true, SubtitleMode Mode = SubtitleMode.Srt)
{
    public static IReadOnlyList<string> DefaultLanguages { get; } = ["en", "hi"];
}

/// <summary>SponsorBlock categories to cut out and to mark as chapters.</summary>
public sealed record SponsorBlockOptions(IReadOnlyList<string> Remove, IReadOnlyList<string> Mark)
{
    public static IReadOnlyList<string> Categories { get; } = ["sponsor", "intro", "outro", "selfpromo", "interaction", "preview", "music_offtopic", "filler"];

    /// <summary>The plan's footage default: remove sponsor, selfpromo and interaction; mark the rest.</summary>
    public static SponsorBlockOptions FootageDefault { get; } = new(
        ["sponsor", "selfpromo", "interaction"],
        ["intro", "outro", "preview", "music_offtopic", "filler"]);
}

/// <param name="FromStart">Record from the start of the stream (<c>--live-from-start</c>) instead of from now.</param>
/// <param name="WaitForVideoSeconds">For a scheduled stream, check this often until it starts (<c>--wait-for-video</c>).</param>
public sealed record LiveOptions(bool FromStart, int? WaitForVideoSeconds = null);

public static class FilenameTemplates
{
    public const string Default = "%(title)s [%(id)s].%(ext)s";

    public static IReadOnlyList<(string Name, string Template)> Presets { get; } =
    [
        ("Title [id]", Default),
        ("Date - Title [id]", "%(upload_date)s - %(title)s [%(id)s].%(ext)s"),
        ("Channel - Title [id]", "%(channel)s - %(title)s [%(id)s].%(ext)s"),
        ("Title", "%(title)s.%(ext)s"),
    ];
}
