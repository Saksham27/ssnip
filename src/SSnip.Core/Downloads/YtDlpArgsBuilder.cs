using System.Globalization;
using System.Text.RegularExpressions;

namespace SSnip.Core.Downloads;

/// <summary>
/// Pure function from <see cref="DownloadOptions"/> to yt-dlp arguments. The UI never builds
/// arguments itself. <c>--ignore-config</c> and the JS runtime are added by the process launch planner.
/// </summary>
public static partial class YtDlpArgsBuilder
{
    /// <summary>Prefix of the JSON progress line, see <see cref="YtDlpOutputParser"/>.</summary>
    public const string ProgressPrefix = "FSPROG ";

    public const string PostProcessPrefix = "FSPP ";

    public const string FilePrefix = "FSFILE ";

    private static readonly string[] Containers = ["mp4", "mkv", "mov", "webm"];

    public static IReadOnlyList<string> Build(DownloadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        var args = new List<string>
        {
            "--no-playlist",
            "--newline",
            "--progress",
            "--progress-template", $"download:{ProgressPrefix}%(progress)j",
            "--progress-template", $"postprocess:{PostProcessPrefix}%(progress)j",
            "--print", $"after_move:{FilePrefix}%(filepath)s",
            "--windows-filenames",
            "--trim-filenames", Invariant(options.TrimFilenames),
            "-N", Invariant(options.ConcurrentFragments),
            "--retries", Invariant(options.Retries),
            "--fragment-retries", Invariant(options.Retries),
            "-P", options.OutputDirectory,
            "-o", options.FilenameTemplate,
        };

        AddFormat(args, options);

        args.Add(options.Overwrite ? "--force-overwrites" : "--no-overwrites");

        if (!string.IsNullOrWhiteSpace(options.RateLimit))
        {
            args.Add("--limit-rate");
            args.Add(options.RateLimit.Trim());
        }

        foreach (var section in options.Sections)
        {
            args.Add("--download-sections");
            args.Add(section);
        }

        // "--" stops option parsing, so a URL can never be read as an option.
        args.Add("--");
        args.Add(options.Url.Trim());
        return args;
    }

    public static bool IsValidRateLimit(string? rate) => string.IsNullOrWhiteSpace(rate) || RateLimitPattern().IsMatch(rate.Trim());

    private static void AddFormat(List<string> args, DownloadOptions options)
    {
        switch (options.Preset)
        {
            case FormatPreset.EditorSafe:
                args.AddRange(["-S", "vcodec:h264,res,fps,acodec:aac"]);
                AddContainer(args, options.MergeContainer ?? "mp4");
                break;
            case FormatPreset.MaxQuality:
                args.AddRange(["-f", "bv*+ba/b"]);
                AddContainer(args, options.MergeContainer ?? "mkv");
                break;
            case FormatPreset.Cap1080:
                args.AddRange(["-S", "res:1080,fps"]);
                AddContainer(args, options.MergeContainer);
                break;
            case FormatPreset.AudioOnly:
                args.AddRange(["-f", "ba/b", "-x", "--audio-format", options.AudioFormat.ToString().ToLowerInvariant(), "--audio-quality", "0"]);
                break;
            case FormatPreset.PreviewProxy:
                args.AddRange(["-f", "bv*[height<=360]+ba/b[height<=360]", "-S", "vcodec:h264"]);
                AddContainer(args, options.MergeContainer ?? "mp4");
                break;
            case FormatPreset.Custom:
                AddCustom(args, options.Custom!);
                AddContainer(args, options.MergeContainer);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Preset, "Unknown preset.");
        }
    }

    private static void AddCustom(List<string> args, FormatSelection selection)
    {
        var video = selection.VideoFormatId?.Trim();
        var audio = selection.AudioFormatId?.Trim();
        var format = (string.IsNullOrEmpty(video), string.IsNullOrEmpty(audio)) switch
        {
            (false, false) => $"{video}+{audio}",
            (false, true) => video,
            (true, false) => audio,
            _ => null,
        };

        if (format is not null)
        {
            args.AddRange(["-f", format]);
        }

        if (!string.IsNullOrWhiteSpace(selection.SortString))
        {
            args.AddRange(["-S", selection.SortString.Trim()]);
        }
    }

    private static void AddContainer(List<string> args, string? container)
    {
        if (container is not null)
        {
            args.AddRange(["--merge-output-format", container]);
        }
    }

    private static void Validate(DownloadOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Url))
        {
            throw new ArgumentException("A URL is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            throw new ArgumentException("An output folder is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.FilenameTemplate))
        {
            throw new ArgumentException("A filename template is required.", nameof(options));
        }

        if (options.Preset == FormatPreset.Custom && (options.Custom is null || options.Custom.IsEmpty))
        {
            throw new ArgumentException("Advanced mode needs a format or a sort string.", nameof(options));
        }

        if (options.MergeContainer is not null && !Containers.Contains(options.MergeContainer))
        {
            throw new ArgumentException($"Unsupported container '{options.MergeContainer}'.", nameof(options));
        }

        if (!IsValidRateLimit(options.RateLimit))
        {
            throw new ArgumentException("Rate limit must look like 500K, 5M or 1.5M.", nameof(options));
        }

        if (options.ConcurrentFragments is < 1 or > 32)
        {
            throw new ArgumentException("Concurrent fragments must be between 1 and 32.", nameof(options));
        }

        if (options.Retries < 0 || options.TrimFilenames < 20)
        {
            throw new ArgumentException("Retries must be 0 or more and filenames at least 20 characters.", nameof(options));
        }
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\d+(\.\d+)?[KMG]?$", RegexOptions.IgnoreCase)]
    private static partial Regex RateLimitPattern();
}
