using Shouldly;
using SSnip.Core.Processes;

namespace SSnip.Core.Tests.Processes;

public class CommandLineFormatterTests
{
    [Theory]
    [InlineData("--version", "--version")]
    [InlineData("", "\"\"")]
    [InlineData("My Video.mp4", "\"My Video.mp4\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\Footage\", @"C:\Footage\")]
    [InlineData(@"C:\My Footage\", "\"C:\\My Footage\\\\\"")]
    [InlineData(@"a\""b", "\"a\\\\\\\"b\"")]
    [InlineData("tab\there", "\"tab\there\"")]
    public void Quote_follows_windows_rules(string argument, string expected) =>
        CommandLineFormatter.Quote(argument).ShouldBe(expected);

    [Fact]
    public void Quote_keeps_hindi_text_unquoted_when_it_has_no_spaces() =>
        CommandLineFormatter.Quote("नमस्ते").ShouldBe("नमस्ते");

    [Fact]
    public void Quote_wraps_hindi_text_with_spaces() =>
        CommandLineFormatter.Quote("मॉडर्न वॉरफेयर रीकैप").ShouldBe("\"मॉडर्न वॉरफेयर रीकैप\"");

    [Fact]
    public void Format_joins_executable_and_arguments()
    {
        var line = CommandLineFormatter.Format(@"C:\Users\Me\AppData\Local\SSnip\bin\yt-dlp.exe", ["-o", "%(title)s [%(id)s].%(ext)s", "--newline"]);

        line.ShouldBe(@"C:\Users\Me\AppData\Local\SSnip\bin\yt-dlp.exe -o ""%(title)s [%(id)s].%(ext)s"" --newline");
    }

    [Fact]
    public void Format_quotes_an_executable_path_with_spaces()
    {
        var line = CommandLineFormatter.Format(@"C:\Program Files\SSnip\bin\ffmpeg.exe", ["-version"]);

        line.ShouldBe(@"""C:\Program Files\SSnip\bin\ffmpeg.exe"" -version");
    }
}
