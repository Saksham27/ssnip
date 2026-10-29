using System.Text.RegularExpressions;

namespace SSnip.Core.Tools;

/// <summary>Extracts a version string from a tool's version output.</summary>
public static partial class ToolVersionParser
{
    public static string? Parse(ExternalTool tool, string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var match = tool switch
        {
            // "2026.10.22" (stable) or "2026.10.22.233104" (nightly)
            ExternalTool.YtDlp => YtDlpVersion().Match(output),
            // "ffmpeg version 8.0-essentials_build-www.gyan.dev Copyright (c) ..."
            ExternalTool.Ffmpeg => FfmpegVersion().Match(output),
            ExternalTool.Ffprobe => FfprobeVersion().Match(output),
            // "deno 2.5.4 (stable, release, x86_64-pc-windows-msvc)"
            ExternalTool.Deno => DenoVersion().Match(output),
            _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null),
        };

        return match.Success ? match.Groups["version"].Value : null;
    }

    [GeneratedRegex(@"^\s*(?<version>\d{4}\.\d{2}\.\d{2}(?:\.\d+)?)\s*$", RegexOptions.Multiline)]
    private static partial Regex YtDlpVersion();

    [GeneratedRegex(@"^ffmpeg version (?<version>\S+)", RegexOptions.Multiline)]
    private static partial Regex FfmpegVersion();

    [GeneratedRegex(@"^ffprobe version (?<version>\S+)", RegexOptions.Multiline)]
    private static partial Regex FfprobeVersion();

    [GeneratedRegex(@"^deno (?<version>\d+\.\d+\.\d+\S*)", RegexOptions.Multiline)]
    private static partial Regex DenoVersion();
}
