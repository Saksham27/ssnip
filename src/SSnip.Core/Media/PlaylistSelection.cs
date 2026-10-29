using System.Globalization;
using System.Text.RegularExpressions;

namespace SSnip.Core.Media;

/// <summary>
/// Narrows a playlist or channel listing. Title and duration come from the <c>--flat-playlist</c>
/// listing; upload dates are not in it, so date filters go to yt-dlp with each job instead.
/// </summary>
/// <param name="Title">Text the title must contain, or <c>/regex/</c>; case is ignored.</param>
public sealed record PlaylistFilter(string? Title = null, TimeSpan? MinDuration = null, TimeSpan? MaxDuration = null)
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Title) && MinDuration is null && MaxDuration is null;

    /// <summary>Entries with an unknown duration pass the duration limits, like yt-dlp's <c>?</c> filters.</summary>
    public bool Matches(PlaylistEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Duration is { } duration && ((MinDuration is { } min && duration < min) || (MaxDuration is { } max && duration > max)))
        {
            return false;
        }

        var title = Title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return true;
        }

        return IsRegex(title)
            ? Regex.IsMatch(entry.Title, title[1..^1], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout)
            : entry.Title.Contains(title, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Null when the title filter is fine, otherwise why it is not.</summary>
    public string? Validate()
    {
        var title = Title?.Trim();
        if (title is not null && IsRegex(title))
        {
            try
            {
                _ = new Regex(title[1..^1], RegexOptions.None, RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                return $"The title pattern is not valid: {ex.Message}";
            }
        }

        return MinDuration > MaxDuration ? "The shortest duration is longer than the longest." : null;
    }

    private static bool IsRegex(string title) => title.Length > 2 && title[0] == '/' && title[^1] == '/';
}

/// <summary>
/// Parses yt-dlp style item lists such as <c>1:10,15,20-22</c>: single items, inclusive ranges with
/// <c>:</c> or <c>-</c>, open ends (<c>5:</c>, <c>:3</c>) and negative indexes counted from the end
/// (<c>-3:</c> is the last three).
/// </summary>
public static partial class PlaylistItems
{
    /// <summary>The chosen 1-based indexes, in playlist order. Throws <see cref="FormatException"/> for bad input.</summary>
    public static IReadOnlyList<int> Parse(string spec, int count)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var chosen = new SortedSet<int>();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Item().Match(part);
            if (!match.Success)
            {
                throw new FormatException($"'{part}' is not an item number or range, for example 3, 1:10 or 20-22.");
            }

            var start = Index(match.Groups["start"].Value, count, 1);
            var end = match.Groups["sep"].Success ? Index(match.Groups["end"].Value, count, count) : start;
            for (var i = Math.Max(1, start); i <= Math.Min(count, end); i++)
            {
                chosen.Add(i);
            }
        }

        return [.. chosen];
    }

    private static int Index(string text, int count, int whenEmpty)
    {
        if (text.Length == 0)
        {
            return whenEmpty;
        }

        var value = int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return value < 0 ? count + value + 1 : value;
    }

    // "5", "-3", "1:10", "20-22", "5:", ":3", "-3:". A dash range needs numbers on both sides.
    [GeneratedRegex(@"^(?<start>-?\d*)(?:(?<sep>:)(?<end>-?\d*)|(?<sep>-)(?<end>\d+))?$")]
    private static partial Regex Item();
}

public enum ChannelTab
{
    Videos,
    Shorts,
    Live,
}

public static partial class ChannelTabs
{
    /// <summary>
    /// The YouTube channel URL for a tab, for example <c>https://www.youtube.com/@name/shorts</c>;
    /// null when the link is not a YouTube channel.
    /// </summary>
    public static string? UrlFor(string channelUrl, ChannelTab tab)
    {
        if (!Uri.TryCreate(channelUrl, UriKind.Absolute, out var uri) || !uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var match = ChannelPath().Match(uri.AbsolutePath);
        if (!match.Success)
        {
            return null;
        }

        var suffix = tab switch
        {
            ChannelTab.Shorts => "shorts",
            ChannelTab.Live => "streams",
            _ => "videos",
        };
        return $"https://www.youtube.com{match.Groups["base"].Value}/{suffix}";
    }

    // /@handle, /channel/UC…, /c/name or /user/name, optionally followed by a tab.
    [GeneratedRegex(@"^(?<base>/(@[^/]+|channel/[^/]+|c/[^/]+|user/[^/]+))(/[^/]*)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelPath();
}
