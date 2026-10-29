namespace SSnip.Core.Downloads;

/// <summary>One line of the input box: a link, or a search term (sent to search).</summary>
public sealed record LinkInput(string Text, bool IsUrl);

public static class LinkInputParser
{
    /// <summary>Splits pasted or dropped text into links, one per line, ignoring blanks and duplicates.</summary>
    public static IReadOnlyList<LinkInput> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inputs = new List<LinkInput>();
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var input = Classify(raw);
            if (seen.Add(input.Text))
            {
                inputs.Add(input);
            }
        }

        return inputs;
    }

    public static LinkInput Classify(string text)
    {
        var trimmed = text.Trim().Trim('<', '>', '"', '\'');

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            return new LinkInput(uri.ToString(), true);
        }

        // "youtu.be/abc" or "www.example.com/video" without a scheme.
        if (!trimmed.Contains(' ', StringComparison.Ordinal)
            && trimmed.Contains('.', StringComparison.Ordinal)
            && trimmed.Contains('/', StringComparison.Ordinal)
            && Uri.TryCreate("https://" + trimmed, UriKind.Absolute, out var withScheme)
            && withScheme.Host.Contains('.', StringComparison.Ordinal))
        {
            return new LinkInput(withScheme.ToString(), true);
        }

        return new LinkInput(trimmed, false);
    }
}
