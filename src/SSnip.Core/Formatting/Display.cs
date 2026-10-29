using System.Globalization;

namespace SSnip.Core.Formatting;

/// <summary>Human-readable sizes, speeds and times for the UI.</summary>
public static class Display
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(long? bytes)
    {
        if (bytes is null or < 0)
        {
            return "–";
        }

        double value = bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{value:0} {Units[unit]}")
            : string.Create(CultureInfo.InvariantCulture, $"{value:0.0} {Units[unit]}");
    }

    public static string Speed(double? bytesPerSecond) =>
        bytesPerSecond is null or <= 0 ? "–" : $"{Bytes((long)bytesPerSecond.Value)}/s";

    /// <summary>"0:35", "4:13", "1:02:15".</summary>
    public static string Duration(TimeSpan? duration)
    {
        if (duration is null)
        {
            return "–";
        }

        var d = duration.Value;
        return d.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{d.Minutes}:{d.Seconds:00}");
    }

    public static string Count(long? count) => count is null ? "–" : count.Value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Bitrate(double? kbps) => kbps is null or <= 0
        ? "–"
        : kbps >= 1000
            ? string.Create(CultureInfo.InvariantCulture, $"{kbps / 1000:0.0} Mbps")
            : string.Create(CultureInfo.InvariantCulture, $"{kbps:0} kbps");

    public static string Percent(double? fraction) =>
        fraction is null ? "–" : string.Create(CultureInfo.InvariantCulture, $"{fraction.Value * 100:0.0}%");
}
