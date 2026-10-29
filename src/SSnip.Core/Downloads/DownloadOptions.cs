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
}

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
