using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SSnip.Core.Media;

namespace SSnip.Core.Downloads;

/// <summary>
/// Approximates the file name yt-dlp will write for a template, for the options panel. Handles the
/// common <c>%(field)s</c> fields and the <c>--windows-filenames</c> character substitutions.
/// </summary>
public static partial class FilenameTemplatePreview
{
    public static string Render(string template, MediaInfo info, string ext)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(info);

        return Field().Replace(template, match =>
        {
            var value = match.Groups["name"].Value switch
            {
                "title" or "fulltitle" => info.Title,
                "id" or "display_id" => info.Id,
                "ext" => ext,
                "channel" or "uploader" => info.Channel,
                "upload_date" => info.UploadDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                "duration_string" => info.Duration is { } d ? Formatting.Display.Duration(d) : null,
                "extractor" or "extractor_key" => info.Extractor,
                _ => null,
            };

            // yt-dlp writes NA for missing fields; values are sanitised, the template's own text is not.
            return value is null ? "NA" : SanitizeForWindows(value);
        });
    }

    /// <summary>yt-dlp's <c>--windows-filenames</c>: reserved characters become their full-width forms.</summary>
    public static string SanitizeForWindows(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '/' or '\\' or '|' or '*' or '<' or '>' or ':' or '?' or '"' => (char)(c + 0xFEE0),
                < ' ' => ' ',
                _ => c,
            });
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"%\((?<name>[a-z_]+)\)s")]
    private static partial Regex Field();
}
