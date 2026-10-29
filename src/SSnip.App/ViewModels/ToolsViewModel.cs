using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SSnip.App.Services;
using SSnip.Core;
using SSnip.Core.Tools;

namespace SSnip.App.ViewModels;

/// <summary>
/// The dependency manager screen, shared by the first-run wizard, Settings › Tools and the status bar.
/// </summary>
public sealed partial class ToolsViewModel : ObservableObject
{
    private readonly IDependencyManager _dependencies;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ToolsViewModel> _logger;

    public ToolsViewModel(IDependencyManager dependencies, IDialogService dialogs, AppPaths paths, ILogger<ToolsViewModel> logger)
    {
        _dependencies = dependencies;
        _dialogs = dialogs;
        _logger = logger;
        BinFolder = paths.Bin;
        Tools = [.. Enum.GetValues<ExternalTool>().Select(t => new ToolRowViewModel(t, paths.ToolPath(t)))];
    }

    public ObservableCollection<ToolRowViewModel> Tools { get; }

    public string BinFolder { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallMissingCommand))]
    public partial bool AllHealthy { get; private set; }

    [ObservableProperty]
    public partial string HealthSummary { get; private set; } = "Checking tools…";

    [ObservableProperty]
    public partial string? JavaScriptRuntime { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        foreach (var row in Tools)
        {
            row.State = ToolRowState.Checking;
            row.StatusText = "Checking…";
        }

        var statuses = await _dependencies.GetAllStatusAsync();
        foreach (var status in statuses)
        {
            Row(status.Tool).Apply(status);
        }

        UpdateHealth(statuses);
    }

    /// <summary>Downloads every missing or broken tool, archives in parallel. Cancellable.</summary>
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(HasMissingTools))]
    private async Task InstallMissingAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;

        // FFmpeg and ffprobe share one archive, so install by archive, not by tool.
        var groups = Tools.Where(r => !r.IsHealthy)
            .GroupBy(r => r.Tool is ExternalTool.Ffprobe ? ExternalTool.Ffmpeg : r.Tool)
            .ToList();

        await Task.WhenAll(groups.Select(g => InstallGroupAsync(g.Key, [.. g], cancellationToken, update: false)));
        await RefreshAsync();
    }

    private bool HasMissingTools() => !AllHealthy;

    [RelayCommand]
    private async Task UpdateAsync(ToolRowViewModel row)
    {
        ErrorMessage = null;
        var rows = row.Tool is ExternalTool.Ffmpeg or ExternalTool.Ffprobe
            ? Tools.Where(r => r.Tool is ExternalTool.Ffmpeg or ExternalTool.Ffprobe).ToList()
            : [row];

        await InstallGroupAsync(row.Tool, rows, CancellationToken.None, update: true);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ImportAsync(ToolRowViewModel row)
    {
        var descriptor = ToolDescriptor.For(row.Tool);
        var source = _dialogs.PickExecutable($"Choose an existing {descriptor.DisplayName}", descriptor.ExecutableName);
        if (source is null)
        {
            return;
        }

        ErrorMessage = null;
        row.State = ToolRowState.Working;
        row.StatusText = "Copying…";
        try
        {
            await _dependencies.ImportAsync(row.Tool, source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ErrorMessage = ex.Message;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenBinFolder() => _dialogs.OpenInExplorer(BinFolder);

    public void ReportError(string message) => ErrorMessage = message;

    public async Task<bool> CheckJavaScriptRuntimeAsync()
    {
        try
        {
            var runtimes = await _dependencies.GetJavaScriptRuntimesAsync();
            JavaScriptRuntime = runtimes.Count == 0 ? null : string.Join(", ", runtimes);
            return runtimes.Count > 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            JavaScriptRuntime = null;
            ErrorMessage = ex.Message;
            return false;
        }
    }

    private async Task InstallGroupAsync(ExternalTool tool, IReadOnlyList<ToolRowViewModel> rows, CancellationToken cancellationToken, bool update)
    {
        // Progress<T> is created on the UI thread, so reports arrive there.
        var progress = new Progress<InstallProgress>(p =>
        {
            foreach (var row in rows)
            {
                row.Report(p);
            }
        });

        try
        {
            if (update)
            {
                await _dependencies.UpdateAsync(tool, progress, cancellationToken);
            }
            else
            {
                await _dependencies.InstallAsync(tool, progress, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var row in rows)
            {
                row.Fail("Cancelled");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            LogInstallFailed(ex, tool);
            ErrorMessage = $"{ToolDescriptor.For(tool).DisplayName}: {ex.Message}";
            foreach (var row in rows)
            {
                row.Fail("Failed");
            }
        }
    }

    private ToolRowViewModel Row(ExternalTool tool) => Tools.Single(r => r.Tool == tool);

    private void UpdateHealth(IReadOnlyCollection<ToolStatus> statuses)
    {
        (AllHealthy, HealthSummary) = ToolHealth.Summarise(statuses);
    }

    private void LogInstallFailed(Exception ex, ExternalTool tool) => _logger.LogError(ex, "Installing {Tool} failed", tool);
}
