using SSnip.Core.Queue;

namespace SSnip.Core.Clipping;

/// <summary>Cut one clip out of a video already on disk.</summary>
public sealed record LocalCutOptions : JobOptions
{
    public required string InputPath { get; init; }

    public required TimeSpan Start { get; init; }

    public required TimeSpan End { get; init; }

    public required string Name { get; init; }

    public CutMode Mode { get; init; } = CutMode.Accurate;

    /// <summary>Encoder for Accurate cuts; Keep original or null means H.264.</summary>
    public EditorProfile? Profile { get; init; }

    public override string Source => InputPath;

    public override JobKind Kind => JobKind.Cut;

    /// <summary>Fast cuts keep the source container; Accurate cuts use the profile's.</summary>
    public string OutputPath
    {
        get
        {
            var extension = Mode == CutMode.Fast
                ? Path.GetExtension(InputPath).TrimStart('.')
                : (Profile is { Converts: true } ? Profile : new EditorProfile(EditorProfileKind.H264)).Extension;
            return Path.Combine(OutputDirectory, $"{FileNames.Safe(Name)}.{extension}");
        }
    }
}

public enum FrameMode
{
    /// <summary>The frame at <see cref="FrameOptions.At"/>.</summary>
    Single,

    /// <summary>One frame every <see cref="FrameOptions.EverySeconds"/> seconds.</summary>
    Every,

    /// <summary>One storyboard image of frames every <see cref="FrameOptions.EverySeconds"/> seconds.</summary>
    ContactSheet,
}

/// <summary>Full-resolution frame grabs from a local file, or from a video URL's best stream.</summary>
public sealed record FrameOptions : JobOptions
{
    /// <summary>A local file, or a video page URL whose best video stream yt-dlp resolves first.</summary>
    public required string Input { get; init; }

    public required string Name { get; init; }

    public FrameMode Mode { get; init; } = FrameMode.Single;

    public TimeSpan At { get; init; }

    public double EverySeconds { get; init; } = 10;

    /// <summary>Needed to size the contact sheet.</summary>
    public TimeSpan? Duration { get; init; }

    public bool IsUrl => Uri.TryCreate(Input, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public override string Source => Input;

    public override JobKind Kind => JobKind.Frames;
}

public static class FileNames
{
    private static readonly char[] Invalid = [.. Path.GetInvalidFileNameChars(), '<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>A Windows-safe file name: invalid characters become <c>_</c>, trailing dots and spaces go, and it stays under 150 characters.</summary>
    public static string Safe(string name)
    {
        var cleaned = string.Concat((name ?? "").Select(c => Invalid.Contains(c) || char.IsControl(c) ? '_' : c)).Trim().TrimEnd('.', ' ');
        if (cleaned.Length > 150)
        {
            cleaned = cleaned[..150].TrimEnd('.', ' ');
        }

        return cleaned.Length == 0 ? "Clip" : cleaned;
    }
}
