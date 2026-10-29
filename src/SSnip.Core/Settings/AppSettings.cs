using System.Text.Json;
using System.Text.Json.Serialization;

namespace SSnip.Core.Settings;

public enum AppTheme
{
    Dark,
    Light,
    System,
}

/// <summary>User settings, stored as <c>settings.json</c>. Later phases add their defaults here.</summary>
public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Folder that holds one sub-folder per project. Null until the first-run wizard sets it.</summary>
    public string? LibraryRoot { get; init; }

    public AppTheme Theme { get; init; } = AppTheme.Dark;

    public bool FirstRunCompleted { get; init; }
}

public interface ISettingsStore
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? Changed;

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public static class SettingsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, Options);

    /// <summary>Reads settings; missing fields keep their defaults. Throws <see cref="JsonException"/> on invalid JSON.</summary>
    public static AppSettings Deserialize(string json) =>
        string.IsNullOrWhiteSpace(json) ? new AppSettings() : JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
}
