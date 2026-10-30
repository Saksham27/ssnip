using System.Text.Json;
using SSnip.Core.Processes;

namespace SSnip.Core.Downloads;

/// <summary>A meaningful line of yt-dlp download output.</summary>
public abstract record YtDlpEvent;

/// <summary>One <c>FSPROG</c> progress line.</summary>
public sealed record DownloadProgressEvent(
    string Status,
    long DownloadedBytes,
    long? TotalBytes,
    bool IsTotalEstimate,
    double? BytesPerSecond,
    TimeSpan? Eta,
    string? FileName,
    int? FragmentIndex,
    int? FragmentCount,
    TimeSpan? Elapsed = null,
    string? VideoCodec = null,
    string? AudioCodec = null) : YtDlpEvent
{
    public bool IsFinished => Status == "finished";

    /// <summary>
    /// yt-dlp reports a file it skipped because it already exists as "finished" with no elapsed time.
    /// A real download always reports how long it took.
    /// </summary>
    public bool IsAlreadyDownloaded => IsFinished && Elapsed is null;

    public double? Fraction => TotalBytes is > 0 ? Math.Clamp((double)DownloadedBytes / TotalBytes.Value, 0, 1) : null;
}

/// <summary>One <c>FSPP</c> post-processing line, for example the Merger starting.</summary>
public sealed record PostProcessEvent(string PostProcessor, bool IsStarted) : YtDlpEvent;

/// <summary>The final file path, printed after yt-dlp moves the file into place.</summary>
public sealed record FinalFileEvent(string Path) : YtDlpEvent;

/// <summary>The title, printed just before the download starts.</summary>
public sealed record TitleEvent(string Title) : YtDlpEvent;

public sealed record ErrorEvent(string Message) : YtDlpEvent;

public sealed record WarningEvent(string Message) : YtDlpEvent;

public static class YtDlpOutputParser
{
    public static YtDlpEvent? Parse(OutputLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var text = line.Text;

        if (text.StartsWith(YtDlpArgsBuilder.ProgressPrefix, StringComparison.Ordinal))
        {
            return ParseProgress(text[YtDlpArgsBuilder.ProgressPrefix.Length..]);
        }

        if (text.StartsWith(YtDlpArgsBuilder.PostProcessPrefix, StringComparison.Ordinal))
        {
            return ParsePostProcess(text[YtDlpArgsBuilder.PostProcessPrefix.Length..]);
        }

        if (text.StartsWith(YtDlpArgsBuilder.FilePrefix, StringComparison.Ordinal))
        {
            var path = text[YtDlpArgsBuilder.FilePrefix.Length..].Trim();
            return path.Length > 0 ? new FinalFileEvent(path) : null;
        }

        if (text.StartsWith(YtDlpArgsBuilder.TitlePrefix, StringComparison.Ordinal))
        {
            var title = text[YtDlpArgsBuilder.TitlePrefix.Length..].Trim();
            return title.Length > 0 ? new TitleEvent(title) : null;
        }

        if (text.StartsWith("ERROR: ", StringComparison.Ordinal))
        {
            return new ErrorEvent(text["ERROR: ".Length..].Trim());
        }

        if (text.StartsWith("WARNING: ", StringComparison.Ordinal))
        {
            return new WarningEvent(text["WARNING: ".Length..].Trim());
        }

        return null;
    }

    private static DownloadProgressEvent? ParseProgress(string text)
    {
        // "vcodec|acodec {json}"; a bare "{json}" is still accepted.
        string? videoCodec = null, audioCodec = null;
        var json = text;
        if (!text.StartsWith('{'))
        {
            var space = text.IndexOf(' ', StringComparison.Ordinal);
            string[] codecs = space < 0 ? [] : text[..space].Split('|');
            if (codecs.Length != 2)
            {
                return null;
            }

            videoCodec = Codec(codecs[0]);
            audioCodec = Codec(codecs[1]);
            json = text[(space + 1)..];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var p = document.RootElement;
            if (p.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var total = Long(p, "total_bytes");
            var estimate = Long(p, "total_bytes_estimate");
            var eta = Double(p, "eta");
            var elapsed = Double(p, "elapsed");

            return new DownloadProgressEvent(
                String(p, "status") ?? "downloading",
                Long(p, "downloaded_bytes") ?? 0,
                total ?? estimate,
                total is null && estimate is not null,
                Double(p, "speed"),
                eta is >= 0 ? TimeSpan.FromSeconds(eta.Value) : null,
                String(p, "filename") ?? String(p, "tmpfilename"),
                (int?)Long(p, "fragment_index"),
                (int?)Long(p, "fragment_count"),
                elapsed is >= 0 ? TimeSpan.FromSeconds(elapsed.Value) : null,
                videoCodec,
                audioCodec);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>yt-dlp prints <c>none</c> for a missing stream and <c>NA</c> for an unknown field.</summary>
    private static string? Codec(string value) => value is "" or "none" or "NA" ? null : value;

    private static PostProcessEvent? ParsePostProcess(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var p = document.RootElement;
            var name = p.ValueKind == JsonValueKind.Object ? String(p, "postprocessor") : null;
            return name is null ? null : new PostProcessEvent(name, String(p, "status") == "started");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? String(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Double(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static long? Long(JsonElement e, string name) => Double(e, name) is { } d ? (long)Math.Round(d) : null;
}
