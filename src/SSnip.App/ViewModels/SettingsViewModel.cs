using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SSnip.App.Services;
using SSnip.Core;
using SSnip.Core.Settings;

namespace SSnip.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly AppPaths _paths;

    public SettingsViewModel(ISettingsStore settings, IDialogService dialogs, AppPaths paths, ToolsViewModel tools)
    {
        _settings = settings;
        _dialogs = dialogs;
        _paths = paths;
        Tools = tools;
        LibraryRoot = settings.Current.LibraryRoot;
        FetchClipboardOnFocus = settings.Current.FetchClipboardOnFocus;
        settings.Changed += (_, s) => LibraryRoot = s.LibraryRoot;
    }

    [ObservableProperty]
    public partial bool FetchClipboardOnFocus { get; set; }

    partial void OnFetchClipboardOnFocusChanged(bool value)
    {
        if (_settings.Current.FetchClipboardOnFocus != value)
        {
            _ = _settings.SaveAsync(_settings.Current with { FetchClipboardOnFocus = value });
        }
    }

    public ToolsViewModel Tools { get; }

    public string AppDataFolder => _paths.Root;

    [ObservableProperty]
    public partial string? LibraryRoot { get; private set; }

    [RelayCommand]
    private async Task ChangeLibraryRootAsync()
    {
        var folder = _dialogs.PickFolder("Choose the library folder for your footage", LibraryRoot);
        if (folder is null)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        await _settings.SaveAsync(_settings.Current with { LibraryRoot = folder });
    }

    [RelayCommand]
    private void OpenLibraryRoot()
    {
        if (LibraryRoot is not null)
        {
            _dialogs.OpenInExplorer(LibraryRoot);
        }
    }

    [RelayCommand]
    private void OpenAppDataFolder() => _dialogs.OpenInExplorer(_paths.Root);

    [RelayCommand]
    private void OpenLogs() => _dialogs.OpenInExplorer(_paths.Logs);
}
