using Shouldly;
using SSnip.Core.Downloads;

namespace SSnip.Core.Tests.Downloads;

public class LinkInputParserTests
{
    [Fact]
    public void Splits_lines_and_drops_blanks_and_duplicates()
    {
        var inputs = LinkInputParser.Parse("https://www.example.com/watch?v=a\r\n\r\n  https://www.example.com/watch?v=b \nhttps://www.example.com/watch?v=a\n");

        inputs.Select(i => i.Text).ShouldBe(["https://www.example.com/watch?v=a", "https://www.example.com/watch?v=b"]);
        inputs.ShouldAllBe(i => i.IsUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public void Empty_input_gives_nothing(string? text) => LinkInputParser.Parse(text).ShouldBeEmpty();

    [Theory]
    [InlineData("https://www.example.com/watch?v=a", "https://www.example.com/watch?v=a")]
    [InlineData("http://example.com/v/1", "http://example.com/v/1")]
    [InlineData("<https://example.com/v/1>", "https://example.com/v/1")]
    [InlineData("\"https://example.com/v/1\"", "https://example.com/v/1")]
    [InlineData("youtu.be/TestVid0001", "https://youtu.be/TestVid0001")]
    [InlineData("www.example.com/watch?v=a", "https://www.example.com/watch?v=a")]
    public void Recognises_links(string text, string expected) =>
        LinkInputParser.Classify(text).ShouldBe(new LinkInput(expected, true));

    [Theory]
    [InlineData("modern warfare soap scene")]
    [InlineData("मॉडर्न वॉरफेयर")]
    [InlineData("ftp://example.com/file")]
    [InlineData("1:02:15")]
    public void Everything_else_is_a_search(string text) =>
        LinkInputParser.Classify(text).IsUrl.ShouldBeFalse();
}
