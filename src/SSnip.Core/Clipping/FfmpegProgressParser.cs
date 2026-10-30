using System.Globalization;

namespace SSnip.Core.Clipping;

/// <summary>One block of ffmpeg <c>-progress</c> output.</summary>
/// <param name="Speed">Encoding speed relative to real time, 2.5 for "2.5x".</param>
public sealed record FfmpegProgress(TimeSpan Position, double? Speed, bool IsEnd)
{
    public double? FractionOf(TimeSpan? duration) =>
        IsEnd ? 1 : duration is { TotalMilliseconds: > 0 } d ? Math.Clamp(Position / d, 0, 1) : null;

    /// <summary>Time left at the current speed.</summary>
    public TimeSpan? EtaFor(TimeSpan? duration) =>
        duration is { } d && Speed is > 0 && d > Position ? TimeSpan.FromSeconds((d - Position).TotalSeconds / Speed.Value) : null;
}

/// <summary>
/// Reads ffmpeg's <c>-progress pipe:1</c> output (<c>key=value</c> lines). A block ends with
/// <c>progress=continue</c> or <c>progress=end</c>, which is when <see cref="Read"/> returns a value.
/// </summary>
public sealed class FfmpegProgressParser
{
    private TimeSpan _position;
    private double? _speed;

    public FfmpegProgress? Read(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var equals = line.IndexOf('=', StringComparison.Ordinal);
        if (equals <= 0)
        {
            return null;
        }

        var key = line[..equals].Trim();
        var value = line[(equals + 1)..].Trim();
        switch (key)
        {
            // out_time_us, and out_time_ms which despite its name is also microseconds.
            case "out_time_us" or "out_time_ms" when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var micro) && micro >= 0:
                _position = TimeSpan.FromTicks(micro * 10);
                break;
            case "speed":
                _speed = double.TryParse(value.TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) ? speed : null;
                break;
            case "progress":
                return new FfmpegProgress(_position, _speed, value == "end");
        }

        return null;
    }
}

public static class EncoderList
{
    /// <summary>Encoder names from <c>ffmpeg -hide_banner -encoders</c>: lines like <c> V....D h264_amf  AMD AMF H.264 Encoder</c>.</summary>
    public static IReadOnlySet<string> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            // Skips the legend (" V..... = Video") and the "------" divider.
            if (parts.Length >= 2 && parts[0].Length == 6 && parts[0][0] is 'V' or 'A' or 'S' && parts[1] != "=")
            {
                names.Add(parts[1]);
            }
        }

        return names;
    }
}
