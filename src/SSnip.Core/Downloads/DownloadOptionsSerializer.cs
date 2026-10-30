using System.Text.Json;
using System.Text.Json.Serialization;

namespace SSnip.Core.Downloads;

/// <summary>Stores a job's <see cref="DownloadOptions"/> as JSON, so a queued job survives a restart unchanged.</summary>
public static class DownloadOptionsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(DownloadOptions options) => JsonSerializer.Serialize(options, Options);

    /// <summary>Throws <see cref="JsonException"/> when the JSON is invalid or misses the URL or folder.</summary>
    public static DownloadOptions Deserialize(string json) =>
        JsonSerializer.Deserialize<DownloadOptions>(json, Options) ?? throw new JsonException("Empty download options.");
}
