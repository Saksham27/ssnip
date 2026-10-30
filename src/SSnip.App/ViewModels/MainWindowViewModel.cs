using CommunityToolkit.Mvvm.ComponentModel;

namespace SSnip.App.ViewModels;

/// <summary>The shell: navigation plus the status bar and taskbar progress.</summary>
public sealed partial class MainWindowViewModel(ToolsViewModel tools, HomeViewModel home, QueueViewModel queue) : ObservableObject
{
    public ToolsViewModel Tools { get; } = tools;

    public HomeViewModel Home { get; } = home;

    /// <summary>Queue summary ("2 running, 5 queued") and taskbar progress.</summary>
    public QueueViewModel Queue { get; } = queue;

    // Account mode arrives in Phase 5; the status bar shows it off until then.
    [ObservableProperty]
    public partial bool IsAccountModeOn { get; private set; }
}
