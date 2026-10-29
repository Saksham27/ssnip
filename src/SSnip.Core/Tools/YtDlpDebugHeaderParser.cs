using System.Text.RegularExpressions;

namespace SSnip.Core.Tools;

/// <summary>Reads facts from the <c>[debug]</c> header that <c>yt-dlp --verbose</c> prints first.</summary>
public static partial class YtDlpDebugHeaderParser
{
    /// <summary>
    /// The JavaScript runtimes yt-dlp will use, for example <c>["deno-2.9.8"]</c>.
    /// Empty when it reports <c>none</c>; null when the line is missing.
    /// </summary>
    public static IReadOnlyList<string>? ParseJsRuntimes(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var match = JsRuntimesLine().Match(output);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["runtimes"].Value.Trim();
        return value.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    [GeneratedRegex(@"^\[debug\] JS runtimes: (?<runtimes>.*?)\s*$", RegexOptions.Multiline)]
    private static partial Regex JsRuntimesLine();
}
