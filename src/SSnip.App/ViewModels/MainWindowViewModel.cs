using CommunityToolkit.Mvvm.ComponentModel;

namespace SSnip.App.ViewModels;

/// <summary>The shell: navigation plus the status bar.</summary>
public sealed partial class MainWindowViewModel(ToolsViewModel tools) : ObservableObject
{
    public ToolsViewModel Tools { get; } = tools;

    // The queue arrives in Phase 2 and account mode in Phase 5; the status bar shows their idle state until then.
    [ObservableProperty]
    public partial string QueueSummary { get; private set; } = "0 running, 0 queued";

    [ObservableProperty]
    public partial bool IsAccountModeOn { get; private set; }
}
