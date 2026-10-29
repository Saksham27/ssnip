namespace SSnip.Core.Media;

public enum MediaKind
{
    Video,
    Live,
    Upcoming,
    Playlist,
    Channel,
}

/// <summary>What <c>yt-dlp -J</c> reports for a link: a single video, or a playlist or channel listing.</summary>
public sealed record MediaInfo
{
    public required MediaKind Kind { get; init; }

    public required string Id { get; init; }

    public required string Title { get; init; }

    public string? Channel { get; init; }

    public string? ChannelUrl { get; init; }

    public string? WebpageUrl { get; init; }

    public string? Extractor { get; init; }

    public TimeSpan? Duration { get; init; }

    public DateOnly? UploadDate { get; init; }

    public long? ViewCount { get; init; }

    /// <summary>yt-dlp's live_status: not_live, is_live, is_upcoming, was_live, post_live.</summary>
    public string? LiveStatus { get; init; }

    public string? ThumbnailUrl { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<ChapterInfo> Chapters { get; init; } = [];

    public IReadOnlyList<SubtitleTrack> Subtitles { get; init; } = [];

    public IReadOnlyList<FormatInfo> Formats { get; init; } = [];

    /// <summary>For playlists and channels: the entries listed with <c>--flat-playlist</c>.</summary>
    public IReadOnlyList<PlaylistEntry> Entries { get; init; } = [];

    public bool IsSingleVideo => Kind is MediaKind.Video or MediaKind.Live or MediaKind.Upcoming;

    public FormatInfo? FindFormat(string formatId) => Formats.FirstOrDefault(f => f.FormatId == formatId);
}

public sealed record ChapterInfo(string Title, TimeSpan Start, TimeSpan End)
{
    public TimeSpan Length => End - Start;
}

public sealed record SubtitleTrack(string Language, string? Name, bool IsAutomatic);

public sealed record PlaylistEntry(string Id, string Title, string? Url, TimeSpan? Duration, string? Channel);

/// <summary>One downloadable format from the <c>formats</c> array.</summary>
public sealed record FormatInfo
{
    public required string FormatId { get; init; }

    public string Ext { get; init; } = "";

    public int? Width { get; init; }

    public int? Height { get; init; }

    public double? Fps { get; init; }

    /// <summary>Video codec, or null for audio-only formats (yt-dlp's "none").</summary>
    public string? VideoCodec { get; init; }

    /// <summary>Audio codec, or null for video-only formats.</summary>
    public string? AudioCodec { get; init; }

    public bool HasVideo { get; init; }

    public bool HasAudio { get; init; }

    /// <summary>Total bitrate in kbit/s.</summary>
    public double? Bitrate { get; init; }

    public long? FileSize { get; init; }

    public bool IsFileSizeApproximate { get; init; }

    public string? DynamicRange { get; init; }

    public string? Protocol { get; init; }

    public string? Note { get; init; }

    public string? Language { get; init; }

    public bool IsAudioOnly => HasAudio && !HasVideo;

    public bool IsVideoOnly => HasVideo && !HasAudio;

    public bool IsStoryboard => Ext == "mhtml" || (Note?.Contains("storyboard", StringComparison.OrdinalIgnoreCase) ?? false);

    public bool IsHdr => DynamicRange is not null && !DynamicRange.Equals("SDR", StringComparison.OrdinalIgnoreCase);

    /// <summary>YouTube's high-bitrate Premium formats say so in format_note; never rely on a format id.</summary>
    public bool IsPremium => Note?.Contains("Premium", StringComparison.OrdinalIgnoreCase) ?? false;

    public string Resolution => HasVideo
        ? Width is > 0 && Height is > 0 ? $"{Width}x{Height}" : Height is > 0 ? $"{Height}p" : "video"
        : "audio only";
}
