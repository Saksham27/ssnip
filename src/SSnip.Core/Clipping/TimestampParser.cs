using System.Globalization;
using System.Text.RegularExpressions;

namespace SSnip.Core.Clipping;

/// <summary>
/// Turns pasted lines such as <c>12:30-13:10 Soap scene</c> into clips, and finds timestamps in a
/// description ("0:00 Intro", "4:12 Boss fight") as candidate clips.
/// </summary>
public static partial class TimestampParser
{
    /// <summary>
    /// One clip per line with a range: <c>12:30-13:10 Name</c>, <c>1:02:15 - 1:03:00 Name</c>,
    /// <c>Name 12:30–13:10</c>, <c>[12:30 → 13:10] Name</c>. Lines with a single time get an end at the
    /// next line's start, or at <paramref name="duration"/> for the last one. Other lines are skipped.
    /// </summary>
    public static IReadOnlyList<ClipMark> ParseLines(string? text, TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var found = new List<(TimeSpan Start, TimeSpan? End, string Name)>();
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Range().Match(line) is { Success: true } range && Time(range.Groups["a"].Value) is { } a && Time(range.Groups["b"].Value) is { } b)
            {
                found.Add((a, b, Name(line, range)));
            }
            else if (Single().Match(line) is { Success: true } single && Time(single.Groups["a"].Value) is { } start)
            {
                found.Add((start, null, Name(line, single)));
            }
        }

        var clips = new List<ClipMark>();
        for (var i = 0; i < found.Count; i++)
        {
            var (start, end, name) = found[i];
            end ??= found.Skip(i + 1).Select(f => (TimeSpan?)f.Start).FirstOrDefault(s => s > start) ?? duration;
            if (end is { } e && e > start)
            {
                clips.Add(new ClipMark { In = start, Out = e, Name = string.IsNullOrEmpty(name) ? $"Clip {clips.Count + 1}" : name });
            }
        }

        return clips;
    }

    /// <summary>
    /// Timestamps in a description or comment: lines that start with (or contain) a time. Each runs to
    /// the next timestamp, so a chapter-style list becomes back-to-back clips.
    /// </summary>
    public static IReadOnlyList<ClipMark> ScanDescription(string? description, TimeSpan? duration) => ParseLines(description, duration);

    /// <summary>Reads <c>ss</c>, <c>m:ss</c>, <c>mm:ss</c> or <c>h:mm:ss</c>, with optional <c>.fff</c>; null if it is not a time.</summary>
    public static TimeSpan? Time(string text)
    {
        var parts = text.Trim().Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return null;
        }

        var seconds = 0.0;
        for (var i = 0; i < parts.Length; i++)
        {
            var isLast = i == parts.Length - 1;
            if (!double.TryParse(parts[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                || (!isLast && parts[i].Contains('.', StringComparison.Ordinal))
                || (i > 0 && value >= 60))
            {
                return null;
            }

            seconds = (seconds * 60) + value;
        }

        return TimeSpan.FromMilliseconds(Math.Round(seconds * 1000));
    }

    private static string Name(string line, Match match)
    {
        var name = (line[..match.Index] + " " + line[(match.Index + match.Length)..]).Trim();
        return SeparatorEdges().Replace(name, "").Trim();
    }

    private const string TimePattern = @"(?:\d{1,2}:)?\d{1,2}:\d{2}(?:\.\d{1,3})?";

    [GeneratedRegex($@"[\[(]?(?<a>{TimePattern})\s*(?:-|–|—|to|→|->)\s*(?<b>{TimePattern})[\])]?", RegexOptions.IgnoreCase)]
    private static partial Regex Range();

    [GeneratedRegex($@"[\[(]?(?<a>(?<!\d){TimePattern})(?!\d)[\])]?")]
    private static partial Regex Single();

    // Leftover separators at either end of a name: "- Soap scene", "Soap scene :", "| Intro".
    [GeneratedRegex(@"^[\s\-–—:|•·]+|[\s\-–—:|•·]+$")]
    private static partial Regex SeparatorEdges();
}
