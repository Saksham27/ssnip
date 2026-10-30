namespace SSnip.Core.Clipping;

/// <summary>The plan's editor-ready profiles: files DaVinci Resolve imports and scrubs well.</summary>
public enum EditorProfileKind
{
    /// <summary>No conversion; the file stays as downloaded.</summary>
    KeepOriginal,

    /// <summary>High-bitrate H.264 MP4: <c>h264_amf</c> on AMD, <c>libx264 -crf 16</c> otherwise.</summary>
    H264,

    /// <summary>DNxHR HQ in MOV: smoothest scrubbing, very large.</summary>
    DnxhrHq,

    /// <summary>ProRes 422 in MOV.</summary>
    ProRes422,
}

public enum ProfileAudio
{
    Aac48k,
    Pcm48k,
}

/// <summary>How to convert a downloaded or cut file for editing.</summary>
/// <param name="Encoder">The H.264 encoder to use, for example <c>h264_amf</c>; null picks <c>libx264</c>.</param>
/// <param name="TargetFps">Frame rate to convert to; null keeps the source's.</param>
/// <param name="TargetHeight">Height to scale to (1080, 1440, 2160), keeping the aspect ratio; null keeps the size.</param>
/// <param name="ConstantFrameRate">Force constant frame rate; YouTube files can be variable, which drifts audio in editors.</param>
public sealed record EditorProfile(
    EditorProfileKind Kind,
    string? Encoder = null,
    int? TargetFps = null,
    int? TargetHeight = null,
    bool ConstantFrameRate = true,
    ProfileAudio Audio = ProfileAudio.Aac48k)
{
    public static EditorProfile Original { get; } = new(EditorProfileKind.KeepOriginal);

    public bool Converts => Kind != EditorProfileKind.KeepOriginal;

    /// <summary>MP4 for H.264 with AAC; MOV for the intermediates and for PCM audio, which MP4 cannot hold.</summary>
    public string Extension => Kind == EditorProfileKind.H264 && Audio == ProfileAudio.Aac48k ? "mp4" : "mov";
}

/// <summary>Fast cuts on keyframes (no re-encode, can be a few seconds off); Accurate re-encodes at the exact marks.</summary>
public enum CutMode
{
    Fast,
    Accurate,
}
