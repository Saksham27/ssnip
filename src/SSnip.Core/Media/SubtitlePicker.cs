namespace SSnip.Core.Media;

/// <summary>
/// Picks one subtitle language from a priority list ("en, hi"). Asking yt-dlp for every listed
/// language fetches machine translations too, which YouTube rate-limits (HTTP 429), and one failed
/// subtitle fails the whole download.
/// </summary>
public static class SubtitlePicker
{
    /// <summary>
    /// The first priority language with a manual track; otherwise, when auto captions are allowed, the
    /// first one that is the video's original spoken language (YouTube marks it <c>xx-orig</c>), then
    /// any auto track. Without known tracks (a playlist entry), the first priority language.
    /// </summary>
    public static string? Pick(IReadOnlyList<string> priority, IReadOnlyList<SubtitleTrack> tracks, bool includeAutomatic)
    {
        ArgumentNullException.ThrowIfNull(priority);
        ArgumentNullException.ThrowIfNull(tracks);

        if (priority.Count == 0)
        {
            return null;
        }

        if (tracks.Count == 0)
        {
            return priority[0];
        }

        var manual = tracks.Where(t => !t.IsAutomatic).Select(t => t.Language).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (priority.FirstOrDefault(manual.Contains) is { } manualLanguage)
        {
            return manualLanguage;
        }

        if (!includeAutomatic)
        {
            return null;
        }

        var automatic = tracks.Where(t => t.IsAutomatic).Select(t => t.Language).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return priority.FirstOrDefault(l => automatic.Contains(l + "-orig"))
            ?? priority.FirstOrDefault(automatic.Contains);
    }
}
