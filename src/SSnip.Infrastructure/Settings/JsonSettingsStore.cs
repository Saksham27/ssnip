using System.Text.Json;
using Microsoft.Extensions.Logging;
using SSnip.Core;
using SSnip.Core.Settings;

namespace SSnip.Infrastructure.Settings;

/// <summary>Reads and writes <c>settings.json</c>. A corrupt file is kept aside and replaced by defaults.</summary>
public sealed partial class JsonSettingsStore(AppPaths paths, ILogger<JsonSettingsStore> logger) : ISettingsStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? Changed;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(paths.SettingsFile))
            {
                return Current = new AppSettings();
            }

            var json = await File.ReadAllTextAsync(paths.SettingsFile, cancellationToken).ConfigureAwait(false);
            try
            {
                return Current = SettingsSerializer.Deserialize(json);
            }
            catch (JsonException ex)
            {
                var backup = paths.SettingsFile + ".corrupt";
                File.Copy(paths.SettingsFile, backup, overwrite: true);
                LogCorrupt(ex, backup);
                return Current = new AppSettings();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(paths.Root);
            var temp = paths.SettingsFile + ".tmp";
            await File.WriteAllTextAsync(temp, SettingsSerializer.Serialize(settings), cancellationToken).ConfigureAwait(false);
            File.Move(temp, paths.SettingsFile, overwrite: true);
            Current = settings;
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, settings);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "settings.json was invalid; saved a copy to {Backup} and loaded defaults")]
    private partial void LogCorrupt(Exception exception, string backup);
}
