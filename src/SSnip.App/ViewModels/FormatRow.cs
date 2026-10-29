using System.Globalization;
using SSnip.Core.Downloads;
using SSnip.Core.Formatting;
using SSnip.Core.Media;

namespace SSnip.App.ViewModels;

/// <summary>A row of the Advanced format table. Numeric properties are the sort keys for their columns.</summary>
public sealed class FormatRow(FormatInfo format)
{
    public FormatInfo Format { get; } = format;

    public string Id => Format.FormatId;

    public string Container => Format.Ext;

    public string Resolution => Format.Resolution;

    public int Height => Format.Height ?? 0;

    public double Fps => Format.Fps ?? 0;

    public string FpsText => Format.Fps is { } fps ? fps.ToString("0.##", CultureInfo.InvariantCulture) : "";

    public string VideoCodec => Format.VideoCodec ?? (Format.HasVideo ? "?" : "");

    public string AudioCodec => Format.AudioCodec ?? (Format.HasAudio ? "?" : "");

    public double Bitrate => Format.Bitrate ?? 0;

    public string BitrateText => Format.Bitrate is null ? "" : Display.Bitrate(Format.Bitrate);

    public long Size => Format.FileSize ?? 0;

    public string SizeText => Format.FileSize is null ? "" : (Format.IsFileSizeApproximate ? "~" : "") + Display.Bytes(Format.FileSize);

    public string Hdr => Format.IsHdr ? Format.DynamicRange! : "";

    public string Protocol => Format.Protocol ?? "";

    public string Note => Format.Note ?? "";

    public bool IsPremium => Format.IsPremium;

    public string Kind => Format switch
    {
        { HasVideo: true, HasAudio: true } => "video + audio",
        { HasVideo: true } => "video",
        _ => "audio",
    };
}

/// <summary>An entry of the Video or Audio picker; <see cref="Format"/> is null for "None".</summary>
public sealed record FormatChoice(FormatInfo? Format, string Label)
{
    public static FormatChoice None(string label) => new(null, label);

    public static FormatChoice For(FormatInfo f)
    {
        var parts = new List<string> { f.FormatId };
        if (f.HasVideo)
        {
            parts.Add(f.Resolution);
            if (f.Fps is { } fps)
            {
                parts.Add($"{fps:0.##}fps".Replace(',', '.'));
            }

            parts.Add(f.VideoCodec ?? "?");
        }

        if (f.HasAudio)
        {
            parts.Add(f.AudioCodec ?? "audio");
            if (f.Language is { } language)
            {
                parts.Add(language);
            }
        }

        if (f.FileSize is not null)
        {
            parts.Add((f.IsFileSizeApproximate ? "~" : "") + Display.Bytes(f.FileSize));
        }

        if (f.IsPremium)
        {
            parts.Add("Premium");
        }

        return new FormatChoice(f, string.Join(" · ", parts));
    }
}

public sealed record PresetOption(FormatPreset Preset, string Name, string Description);
