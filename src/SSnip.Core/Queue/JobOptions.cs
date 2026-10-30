using System.Text.Json;
using System.Text.Json.Serialization;
using SSnip.Core.Clipping;
using SSnip.Core.Downloads;

namespace SSnip.Core.Queue;

public enum JobKind
{
    /// <summary>A yt-dlp download: a video, a clip section or a live recording.</summary>
    Download,

    /// <summary>An ffmpeg cut of a file already on disk.</summary>
    Cut,

    /// <summary>Frame grabs: one frame, one every N seconds, or a contact sheet.</summary>
    Frames,
}

/// <summary>What a queue job does. Each kind has its own options; the store keeps the kind next to the JSON.</summary>
public abstract record JobOptions
{
    public required string OutputDirectory { get; init; }

    /// <summary>What the job reads: a URL or a local file.</summary>
    [JsonIgnore]
    public abstract string Source { get; }

    [JsonIgnore]
    public abstract JobKind Kind { get; }
}

public static class JobOptionsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(JobOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return JsonSerializer.Serialize(options, options.GetType(), Options);
    }

    /// <summary>Throws <see cref="JsonException"/> when the JSON is invalid or misses required fields.</summary>
    public static JobOptions Deserialize(JobKind kind, string json) => (JobOptions?)(kind switch
    {
        JobKind.Download => JsonSerializer.Deserialize<DownloadOptions>(json, Options) as JobOptions,
        JobKind.Cut => JsonSerializer.Deserialize<LocalCutOptions>(json, Options),
        JobKind.Frames => JsonSerializer.Deserialize<FrameOptions>(json, Options),
        _ => throw new JsonException($"Unknown job kind {kind}."),
    }) ?? throw new JsonException("Empty job options.");
}
