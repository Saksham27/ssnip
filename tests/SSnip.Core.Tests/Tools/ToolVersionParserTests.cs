using Shouldly;
using SSnip.Core.Tools;

namespace SSnip.Core.Tests.Tools;

public class ToolVersionParserTests
{
    [Theory]
    [InlineData("2026.10.22\r\n", "2026.10.22")]
    [InlineData("2026.10.22.233104\n", "2026.10.22.233104")]
    [InlineData("WARNING: something\n2026.01.31\n", "2026.01.31")]
    public void Parses_yt_dlp(string output, string expected) =>
        ToolVersionParser.Parse(ExternalTool.YtDlp, output).ShouldBe(expected);

    [Fact]
    public void Parses_ffmpeg()
    {
        const string output = """
            ffmpeg version 8.0-essentials_build-www.gyan.dev Copyright (c) 2000-2025 the FFmpeg developers
            built with gcc 15.2.0 (Rev8, Built by MSYS2 project)
            libavutil      60.  8.100 / 60.  8.100
            """;

        ToolVersionParser.Parse(ExternalTool.Ffmpeg, output).ShouldBe("8.0-essentials_build-www.gyan.dev");
    }

    [Fact]
    public void Parses_ffprobe() =>
        ToolVersionParser.Parse(ExternalTool.Ffprobe, "ffprobe version 7.1.1-full_build-www.gyan.dev Copyright (c) 2007-2025").ShouldBe("7.1.1-full_build-www.gyan.dev");

    [Fact]
    public void Parses_deno()
    {
        const string output = """
            deno 2.5.4 (stable, release, x86_64-pc-windows-msvc)
            v8 14.0.365.5-rusty
            typescript 5.9.2
            """;

        ToolVersionParser.Parse(ExternalTool.Deno, output).ShouldBe("2.5.4");
    }

    [Theory]
    [InlineData(ExternalTool.YtDlp, "")]
    [InlineData(ExternalTool.YtDlp, "yt-dlp: error: no such option")]
    [InlineData(ExternalTool.Ffmpeg, "ffprobe version 8.0")]
    [InlineData(ExternalTool.Ffprobe, "ffmpeg version 8.0")]
    [InlineData(ExternalTool.Deno, "error: unrecognized subcommand")]
    public void Returns_null_for_unexpected_output(ExternalTool tool, string output) =>
        ToolVersionParser.Parse(tool, output).ShouldBeNull();
}
