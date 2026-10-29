using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SSnip.App.Services;
using SSnip.Core.Settings;

namespace SSnip.App.ViewModels;

public enum FirstRunStep
{
    Library,
    Tools,
    Check,
}

/// <summary>
/// First-run wizard: library folder, tool downloads, self-check. The test section download joins the
/// self-check in Phase 1 and the account-mode browser step in Phase 5.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;

    public FirstRunViewModel(ISettingsStore settings, IDialogService dialogs, ToolsViewModel tools)
    {
        _settings = settings;
        _dialogs = dialogs;
        Tools = tools;
        LibraryRoot = settings.Current.LibraryRoot ?? SuggestLibraryRoot();
        tools.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ToolsViewModel.AllHealthy))
            {
                NextCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public event EventHandler? Completed;

    public ToolsViewModel Tools { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryStep), nameof(IsToolsStep), nameof(IsCheckStep), nameof(StepNumber))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    public partial FirstRunStep Step { get; private set; } = FirstRunStep.Library;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string LibraryRoot { get; set; }

    [ObservableProperty]
    public partial string? LibraryError { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FinishCommand))]
    public partial bool? SelfCheckPassed { get; private set; }

    [ObservableProperty]
    public partial bool IsSelfChecking { get; private set; }

    public bool IsLibraryStep => Step == FirstRunStep.Library;

    public bool IsToolsStep => Step == FirstRunStep.Tools;

    public bool IsCheckStep => Step == FirstRunStep.Check;

    public int StepNumber => (int)Step + 1;

    [RelayCommand]
    private void Browse()
    {
        var folder = _dialogs.PickFolder("Choose the library folder for your footage", LibraryRoot);
        if (folder is not null)
        {
            LibraryRoot = folder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        switch (Step)
        {
            case FirstRunStep.Library when TryCreateLibraryRoot():
                Step = FirstRunStep.Tools;
                if (!Tools.AllHealthy)
                {
                    Tools.InstallMissingCommand.Execute(null);
                }

                break;
            case FirstRunStep.Tools:
                Step = FirstRunStep.Check;
                await RunSelfCheckAsync();
                break;
        }
    }

    private bool CanGoNext() => Step switch
    {
        FirstRunStep.Library => !string.IsNullOrWhiteSpace(LibraryRoot),
        FirstRunStep.Tools => Tools.AllHealthy,
        _ => false,
    };

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Step = Step - 1;

    private bool CanGoBack() => Step > FirstRunStep.Library;

    [RelayCommand]
    private async Task RunSelfCheckAsync()
    {
        IsSelfChecking = true;
        SelfCheckPassed = null;
        try
        {
            await Tools.RefreshAsync();
            var hasRuntime = await Tools.CheckJavaScriptRuntimeAsync();
            SelfCheckPassed = Tools.AllHealthy && hasRuntime;
        }
        finally
        {
            IsSelfChecking = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanFinish))]
    private async Task FinishAsync()
    {
        await _settings.SaveAsync(_settings.Current with { LibraryRoot = LibraryRoot, FirstRunCompleted = true });
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private bool CanFinish() => SelfCheckPassed == true;

    private bool TryCreateLibraryRoot()
    {
        LibraryError = null;
        try
        {
            if (!Path.IsPathFullyQualified(LibraryRoot))
            {
                LibraryError = "Enter a full path, for example D:\\Footage.";
                return false;
            }

            LibraryRoot = Path.GetFullPath(LibraryRoot);
            Directory.CreateDirectory(LibraryRoot);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            LibraryError = $"Cannot use this folder: {ex.Message}";
            return false;
        }
    }

    private static string SuggestLibraryRoot()
    {
        // Prefer a second drive, where footage usually lives; otherwise the user's Videos folder.
        var dataDrive = DriveInfo.GetDrives()
            .FirstOrDefault(d => d.DriveType == DriveType.Fixed && d.IsReady && !d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase));
        return dataDrive is not null
            ? Path.Combine(dataDrive.RootDirectory.FullName, "Footage")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "SSnip");
    }
}
