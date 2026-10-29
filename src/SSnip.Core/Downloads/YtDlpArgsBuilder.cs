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

    public const string TitlePrefix = "FSTITLE ";

    /// <summary>info.json files are named by site and id, so one folder can hold every source.</summary>
    public const string InfoJsonTemplate = "%(extractor)s-%(id)s";

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
            // The codecs of the format being downloaded tell the video part from the audio part.
            "--progress-template", $"download:{ProgressPrefix}%(info.vcodec)s|%(info.acodec)s %(progress)j",
            "--progress-template", $"postprocess:{PostProcessPrefix}%(progress)j",
            "--print", $"before_dl:{TitlePrefix}%(title)s",
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

        AddSubtitles(args, options.Subtitles);
        AddMetadata(args, options);
        AddSponsorBlock(args, options);
        AddFilters(args, options);
        AddLive(args, options.Live);

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

    private static void AddSubtitles(List<string> args, SubtitleOptions? subtitles)
    {
        if (subtitles is null || subtitles.Languages.Count == 0)
        {
            return;
        }

        args.Add("--write-subs");
        if (subtitles.IncludeAutomatic)
        {
            args.Add("--write-auto-subs");
        }

        args.AddRange(["--sub-langs", string.Join(",", subtitles.Languages.Select(l => l.Trim()))]);
        args.AddRange(["--convert-subs", "srt"]);
        if (subtitles.Mode is SubtitleMode.Embed or SubtitleMode.SrtAndEmbed)
        {
            args.Add("--embed-subs");
        }

        // --embed-subs deletes the .srt files after embedding unless told to keep them.
        if (subtitles.Mode == SubtitleMode.Embed)
        {
            args.AddRange(["--compat-options", "no-keep-subs"]);
        }
    }

    private static void AddMetadata(List<string> args, DownloadOptions options)
    {
        if (options.EmbedMetadata)
        {
            args.Add("--embed-metadata");
        }

        if (options.EmbedThumbnail && !(options.Preset == FormatPreset.AudioOnly && options.AudioFormat == AudioFormat.Wav))
        {
            args.Add("--embed-thumbnail");
        }

        if (options.EmbedChapters)
        {
            args.Add("--embed-chapters");
        }

        if (options.InfoJsonDirectory is { } infoDirectory)
        {
            args.AddRange(["--write-info-json", "-P", $"infojson:{infoDirectory}", "-o", $"infojson:{InfoJsonTemplate}"]);

            if (options.MaxComments is { } maxComments)
            {
                args.AddRange(["--write-description", "-P", $"description:{infoDirectory}", "-o", $"description:{InfoJsonTemplate}"]);
                args.AddRange(["--write-comments", "--extractor-args", $"youtube:max_comments={Invariant(maxComments)}"]);
            }
        }
    }

    private static void AddSponsorBlock(List<string> args, DownloadOptions options)
    {
        if (options.SponsorBlock is not { } sponsorBlock)
        {
            return;
        }

        // Cutting segments out shifts every later timestamp, so proxies and section downloads, whose
        // timestamps must match the original video, only ever mark them.
        var keepsTimeline = options.Preset == FormatPreset.PreviewProxy || options.Sections.Count > 0;
        IReadOnlyList<string> remove = keepsTimeline ? [] : sponsorBlock.Remove;
        List<string> mark = [.. sponsorBlock.Mark.Union(keepsTimeline ? sponsorBlock.Remove : []).Except(remove)];

        if (remove.Count > 0)
        {
            args.AddRange(["--sponsorblock-remove", string.Join(",", remove)]);
        }

        if (mark.Count > 0)
        {
            args.AddRange(["--sponsorblock-mark", string.Join(",", mark)]);
        }
    }

    private static void AddFilters(List<string> args, DownloadOptions options)
    {
        if (options.DateAfter is { } after)
        {
            args.AddRange(["--dateafter", after.ToString("yyyyMMdd", CultureInfo.InvariantCulture)]);
        }

        if (options.DateBefore is { } before)
        {
            args.AddRange(["--datebefore", before.ToString("yyyyMMdd", CultureInfo.InvariantCulture)]);
        }
    }

    private static void AddLive(List<string> args, LiveOptions? live)
    {
        if (live is null)
        {
            return;
        }

        if (live.FromStart)
        {
            args.Add("--live-from-start");
        }

        if (live.WaitForVideoSeconds is { } seconds)
        {
            args.AddRange(["--wait-for-video", Invariant(seconds)]);
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

        if (options.Subtitles is { } subtitles && subtitles.Languages.Any(l => !LanguagePattern().IsMatch(l.Trim())))
        {
            throw new ArgumentException("Subtitle languages must be codes such as en, hi or pt-BR.", nameof(options));
        }

        if (options.SponsorBlock is { } sponsorBlock && sponsorBlock.Remove.Concat(sponsorBlock.Mark).Any(c => !SponsorBlockOptions.Categories.Contains(c)))
        {
            throw new ArgumentException("Unknown SponsorBlock category.", nameof(options));
        }

        if (options.MaxComments is not null && (options.InfoJsonDirectory is null || options.MaxComments < 1))
        {
            throw new ArgumentException("Saving comments needs an info folder and a limit of at least 1.", nameof(options));
        }

        if (options.DateAfter > options.DateBefore)
        {
            throw new ArgumentException("The upload date range ends before it starts.", nameof(options));
        }

        if (options.Live is { WaitForVideoSeconds: < 1 })
        {
            throw new ArgumentException("Waiting for a stream must check at least every second.", nameof(options));
        }
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\d+(\.\d+)?[KMG]?$", RegexOptions.IgnoreCase)]
    private static partial Regex RateLimitPattern();

    // Codes with an optional region or variant (en, hi, pt-BR, en-orig); no regex or commas from the UI.
    [GeneratedRegex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    private static partial Regex LanguagePattern();
}
