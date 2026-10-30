using System.Globalization;

namespace SSnip.Core.Clipping;

/// <summary>A marked clip: in and out points on the source, with a name, tags and a note.</summary>
public sealed record ClipMark
{
    public required TimeSpan In { get; init; }

    public required TimeSpan Out { get; init; }

    public string Name { get; init; } = "";

    public string Tags { get; init; } = "";

    public string Note { get; init; } = "";

    public TimeSpan Length => Out - In;

    public bool IsValid => In >= TimeSpan.Zero && Out > In;

    /// <summary>
    /// The range to download or cut: the marks widened by <paramref name="handles"/> on both sides
    /// for transitions, kept inside the source.
    /// </summary>
    public (TimeSpan Start, TimeSpan End) WithHandles(TimeSpan handles, TimeSpan? sourceDuration)
    {
        var start = In - handles < TimeSpan.Zero ? TimeSpan.Zero : In - handles;
        var end = Out + handles;
        if (sourceDuration is { } duration && end > duration)
        {
            end = duration;
        }

        return (start, end);
    }
}

public static class ClipTime
{
    public const double MaxHandleSeconds = 5;

    public const double DefaultHandleSeconds = 2;

    /// <summary>yt-dlp's <c>--download-sections</c> value for a time range, to the millisecond: <c>*0:12:30.000-0:13:10.500</c>.</summary>
    public static string Section(TimeSpan start, TimeSpan end) => $"*{Format(start)}-{Format(end)}";

    /// <summary><c>h:mm:ss.fff</c>, the form yt-dlp and ffmpeg both read.</summary>
    public static string Format(TimeSpan time) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}");

    /// <summary>Seconds with three decimals, for ffmpeg's <c>-ss</c> and <c>-t</c>.</summary>
    public static string Seconds(TimeSpan time) => time.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>Short display form: <c>12:30.250</c> or <c>1:02:15.000</c>.</summary>
    public static string Display(TimeSpan time) => time.TotalHours >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}")
        : string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds:000}");
}
