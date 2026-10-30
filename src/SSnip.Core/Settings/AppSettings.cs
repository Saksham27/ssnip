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

    /// <summary>When the app gets focus, fetch a link found on the clipboard.</summary>
    public bool FetchClipboardOnFocus { get; init; }

    /// <summary>Queue jobs that run at once, 1 to 5.</summary>
    public int MaxConcurrentDownloads { get; init; } = Queue.JobScheduler.DefaultConcurrency;

    /// <summary>Subtitle languages picked by default, in order, as typed: "en, hi". A string keeps the record's equality simple.</summary>
    public string SubtitleLanguages { get; init; } = string.Join(", ", Downloads.SubtitleOptions.DefaultLanguages);

    /// <summary>Clip editor defaults.</summary>
    public ClippingSettings Clipping { get; init; } = new();
}

/// <summary>Handles, cut mode and editor profile the clip editor starts with.</summary>
public sealed record ClippingSettings
{
    public double HandleSeconds { get; init; } = Core.Clipping.ClipTime.DefaultHandleSeconds;

    public Core.Clipping.CutMode CutMode { get; init; } = Core.Clipping.CutMode.Accurate;

    public Core.Clipping.EditorProfileKind Profile { get; init; } = Core.Clipping.EditorProfileKind.H264;

    /// <summary>Null keeps the source's frame rate.</summary>
    public int? TargetFps { get; init; }

    /// <summary>Null keeps the source's size.</summary>
    public int? TargetHeight { get; init; }

    public bool ConstantFrameRate { get; init; } = true;

    public Core.Clipping.ProfileAudio Audio { get; init; } = Core.Clipping.ProfileAudio.Aac48k;

    /// <summary>An H.264 encoder to use instead of the detected one, for example <c>libx264</c>; null uses the best working one.</summary>
    public string? H264Encoder { get; init; }
}

public static class LibraryFolders
{
    public const string Raw = "raw";

    public const string Proxies = "proxies";

    public const string Clips = "clips";

    public const string Frames = "frames";

    /// <summary>
    /// <c>&lt;library&gt;\Inbox\&lt;kind&gt;</c>, where everything goes until projects arrive in Phase 4. Without a
    /// library yet, <c>Videos\SSnip\Inbox</c>.
    /// </summary>
    public static string Inbox(AppSettings settings, string kind)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var root = settings.LibraryRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "SSnip");
        return Path.Combine(root, "Inbox", kind);
    }
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
