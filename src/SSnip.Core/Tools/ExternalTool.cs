namespace SSnip.Core.Tools;

/// <summary>The external programs SSnip drives. Every one of them runs through the process runner.</summary>
public enum ExternalTool
{
    YtDlp,
    Ffmpeg,
    Ffprobe,
    Deno,
}

/// <summary>Static facts about an external tool: its file name and how to ask for its version.</summary>
public sealed record ToolDescriptor(ExternalTool Tool, string DisplayName, string ExecutableName, IReadOnlyList<string> VersionArguments)
{
    private static readonly IReadOnlyDictionary<ExternalTool, ToolDescriptor> Descriptors = new Dictionary<ExternalTool, ToolDescriptor>
    {
        [ExternalTool.YtDlp] = new(ExternalTool.YtDlp, "yt-dlp", "yt-dlp.exe", ["--version"]),
        [ExternalTool.Ffmpeg] = new(ExternalTool.Ffmpeg, "FFmpeg", "ffmpeg.exe", ["-hide_banner", "-version"]),
        [ExternalTool.Ffprobe] = new(ExternalTool.Ffprobe, "ffprobe", "ffprobe.exe", ["-hide_banner", "-version"]),
        [ExternalTool.Deno] = new(ExternalTool.Deno, "Deno", "deno.exe", ["--version"]),
    };

    public static IReadOnlyList<ToolDescriptor> All { get; } = [.. Descriptors.Values];

    public static ToolDescriptor For(ExternalTool tool) => Descriptors[tool];
}
