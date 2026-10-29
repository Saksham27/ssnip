using SSnip.Core.Tools;

namespace SSnip.Infrastructure.Tools;

/// <summary>Where each tool is downloaded from. Always the latest official build, never redistributed.</summary>
public sealed record ToolSource(Uri Url, bool IsZip, IReadOnlyList<ExternalTool> Provides);

public static class ToolSources
{
    public static ToolSource YtDlp { get; } = new(
        new Uri("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe"),
        IsZip: false,
        [ExternalTool.YtDlp]);

    // gyan.dev release "essentials": a current release build with libx264 and the AMD, NVIDIA and Intel encoders.
    public static ToolSource Ffmpeg { get; } = new(
        new Uri("https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"),
        IsZip: true,
        [ExternalTool.Ffmpeg, ExternalTool.Ffprobe]);

    public static ToolSource Deno { get; } = new(
        new Uri("https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip"),
        IsZip: true,
        [ExternalTool.Deno]);

    public static ToolSource For(ExternalTool tool) => tool switch
    {
        ExternalTool.YtDlp => YtDlp,
        ExternalTool.Ffmpeg or ExternalTool.Ffprobe => Ffmpeg,
        ExternalTool.Deno => Deno,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null),
    };
}
