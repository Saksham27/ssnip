using System.Globalization;
using System.Text.Json;

namespace SSnip.Core.Media;

/// <summary>Parses the JSON printed by <c>yt-dlp -J</c> (with or without <c>--flat-playlist</c>).</summary>
public static class MediaInfoParser
{
    private static readonly string[] ChannelTabs = ["videos", "shorts", "streams", "featured", "live", "playlists"];

    public static MediaInfo Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var isPlaylist = String(root, "_type") is "playlist" or "multi_video";
        var liveStatus = String(root, "live_status");

        return new MediaInfo
        {
            Kind = isPlaylist ? PlaylistKind(root) : liveStatus switch
            {
                "is_live" => MediaKind.Live,
                "is_upcoming" => MediaKind.Upcoming,
                _ => MediaKind.Video,
            },
            Id = String(root, "id") ?? "",
            Title = String(root, "title") ?? String(root, "id") ?? "Untitled",
            Channel = String(root, "channel") ?? String(root, "uploader"),
            ChannelUrl = String(root, "channel_url") ?? String(root, "uploader_url"),
            WebpageUrl = String(root, "webpage_url") ?? String(root, "original_url"),
            Extractor = String(root, "extractor_key") ?? String(root, "extractor"),
            Duration = Seconds(root, "duration"),
            UploadDate = Date(String(root, "upload_date")),
            ViewCount = Long(root, "view_count"),
            LiveStatus = liveStatus,
            ThumbnailUrl = BestThumbnail(root),
            Description = String(root, "description"),
            Chapters = Chapters(root),
            Subtitles = [.. Subtitles(root, "subtitles", automatic: false), .. Subtitles(root, "automatic_captions", automatic: true)],
            Formats = Formats(root),
            Entries = isPlaylist ? Entries(root) : [],
        };
    }

    private static MediaKind PlaylistKind(JsonElement root)
    {
        var id = String(root, "id") ?? "";
        var basename = String(root, "webpage_url_basename") ?? "";
        var url = String(root, "webpage_url") ?? "";

        // Channel pages list as playlists; YouTube channel ids start with UC, and channel tabs end in /videos etc.
        var isChannel = (id.StartsWith("UC", StringComparison.Ordinal) && id.Length == 24)
            || ChannelTabs.Contains(basename, StringComparer.OrdinalIgnoreCase)
            || url.Contains("/@", StringComparison.Ordinal)
            || url.Contains("/channel/", StringComparison.Ordinal);
        return isChannel ? MediaKind.Channel : MediaKind.Playlist;
    }

    private static List<FormatInfo> Formats(JsonElement root)
    {
        if (!root.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<FormatInfo>();
        foreach (var f in formats.EnumerateArray())
        {
            var vcodec = String(f, "vcodec");
            var acodec = String(f, "acodec");
            var height = Int(f, "height");
            var ext = String(f, "ext") ?? "";
            var isStoryboard = ext == "mhtml";

            // "none" means the stream is absent; a missing codec means yt-dlp does not know it (common for HLS).
            var hasVideo = !isStoryboard && (vcodec is not null ? vcodec != "none" : height is > 0);
            var hasAudio = !isStoryboard && (acodec is not null ? acodec != "none" : vcodec == "none" || String(f, "resolution") == "audio only");

            var size = Long(f, "filesize");
            var approx = Long(f, "filesize_approx");

            list.Add(new FormatInfo
            {
                FormatId = String(f, "format_id") ?? "",
                Ext = ext,
                Width = Int(f, "width"),
                Height = height,
                Fps = Double(f, "fps"),
                VideoCodec = hasVideo ? vcodec : null,
                AudioCodec = hasAudio ? acodec : null,
                HasVideo = hasVideo,
                HasAudio = hasAudio,
                Bitrate = Double(f, "tbr"),
                FileSize = size ?? approx,
                IsFileSizeApproximate = size is null && approx is not null,
                DynamicRange = String(f, "dynamic_range"),
                Protocol = String(f, "protocol"),
                Note = String(f, "format_note"),
                Language = String(f, "language"),
            });
        }

        return list;
    }

    private static List<ChapterInfo> Chapters(JsonElement root)
    {
        if (!root.TryGetProperty("chapters", out var chapters) || chapters.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. chapters.EnumerateArray()
            .Select(c => new ChapterInfo(
                String(c, "title") ?? "",
                Seconds(c, "start_time") ?? TimeSpan.Zero,
                Seconds(c, "end_time") ?? TimeSpan.Zero))];
    }

    private static IEnumerable<SubtitleTrack> Subtitles(JsonElement root, string property, bool automatic)
    {
        if (!root.TryGetProperty(property, out var subtitles) || subtitles.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var language in subtitles.EnumerateObject())
        {
            // live_chat is a chat replay, not subtitles.
            if (language.Name == "live_chat")
            {
                continue;
            }

            var name = language.Value.ValueKind == JsonValueKind.Array
                ? language.Value.EnumerateArray().Select(t => String(t, "name")).FirstOrDefault(n => n is not null)
                : null;
            yield return new SubtitleTrack(language.Name, name, automatic);
        }
    }

    private static List<PlaylistEntry> Entries(JsonElement root)
    {
        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. entries.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.Object)
            .Select(e => new PlaylistEntry(
                String(e, "id") ?? "",
                String(e, "title") ?? String(e, "id") ?? "",
                String(e, "url") ?? String(e, "webpage_url"),
                Seconds(e, "duration"),
                String(e, "channel") ?? String(e, "uploader")))];
    }

    /// <summary>
    /// The widest JPEG or PNG thumbnail. WPF cannot always decode WebP, which YouTube's default
    /// <c>thumbnail</c> often is.
    /// </summary>
    private static string? BestThumbnail(JsonElement root)
    {
        if (root.TryGetProperty("thumbnails", out var thumbnails) && thumbnails.ValueKind == JsonValueKind.Array)
        {
            var best = thumbnails.EnumerateArray()
                .Select(t => (Url: String(t, "url"), Width: Int(t, "width") ?? 0, Preference: Int(t, "preference") ?? 0))
                .Where(t => t.Url is not null && IsDecodableImage(t.Url))
                .OrderByDescending(t => t.Width)
                .ThenByDescending(t => t.Preference)
                .Select(t => t.Url)
                .FirstOrDefault();
            if (best is not null)
            {
                return best;
            }
        }

        return String(root, "thumbnail");
    }

    private static bool IsDecodableImage(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        return path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }

    private static DateOnly? Date(string? yyyymmdd) =>
        DateOnly.TryParseExact(yyyymmdd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Double(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static int? Int(JsonElement element, string name) =>
        Double(element, name) is { } d ? (int)Math.Round(d) : null;

    private static long? Long(JsonElement element, string name) =>
        Double(element, name) is { } d ? (long)Math.Round(d) : null;

    private static TimeSpan? Seconds(JsonElement element, string name) =>
        Double(element, name) is { } d && d >= 0 ? TimeSpan.FromSeconds(d) : null;
}
