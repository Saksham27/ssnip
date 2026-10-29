using Shouldly;
using SSnip.Core.Tools;

namespace SSnip.Core.Tests.Tools;

public class YtDlpDebugHeaderParserTests
{
    private const string Header = """
        [debug] Command-line config: ['--ignore-config', '--verbose', '--simulate', 'about:blank']
        [debug] Encodings: locale cp1252, fs utf-8, pref cp1252, out utf-8, error utf-8, screen utf-8
        [debug] yt-dlp version stable@2026.10.26 from yt-dlp/yt-dlp [d2f1a7c03] (win_exe)
        [debug] Optional libraries: Cryptodome-3.24.1, brotli-1.2.1, yt_dlp_ejs-0.8.0
        [debug] JS runtimes: {0}
        [debug] Proxy map: {{}}
        """;

    [Fact]
    public void Parses_a_single_runtime() =>
        YtDlpDebugHeaderParser.ParseJsRuntimes(string.Format(Header, "deno-2.9.8")).ShouldBe(["deno-2.9.8"]);

    [Fact]
    public void Parses_several_runtimes() =>
        YtDlpDebugHeaderParser.ParseJsRuntimes(string.Format(Header, "deno-2.9.8, node-24.1.0")).ShouldBe(["deno-2.9.8", "node-24.1.0"]);

    [Fact]
    public void None_means_empty() =>
        YtDlpDebugHeaderParser.ParseJsRuntimes(string.Format(Header, "none")).ShouldBeEmpty();

    [Fact]
    public void Missing_line_is_null() =>
        YtDlpDebugHeaderParser.ParseJsRuntimes("[debug] yt-dlp version stable@2026.10.26").ShouldBeNull();

    [Fact]
    public void Handles_windows_line_endings() =>
        YtDlpDebugHeaderParser.ParseJsRuntimes("[debug] JS runtimes: deno-2.9.8\r\n[debug] Proxy map: {}\r\n").ShouldBe(["deno-2.9.8"]);
}
