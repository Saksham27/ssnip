using CommunityToolkit.Mvvm.ComponentModel;

namespace SSnip.App.ViewModels;

/// <summary>The shell: navigation plus the status bar.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(ToolsViewModel tools, HomeViewModel home)
    {
        Tools = tools;
        Home = home;

        // Phase 1 runs one download from Home; the queue replaces this summary in Phase 2.
        home.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HomeViewModel.IsRunning))
            {
                QueueSummary = home.IsRunning ? "1 running, 0 queued" : "0 running, 0 queued";
            }
        };
    }

    public ToolsViewModel Tools { get; }

    public HomeViewModel Home { get; }

    [ObservableProperty]
    public partial string QueueSummary { get; private set; } = "0 running, 0 queued";

    // Account mode arrives in Phase 5; the status bar shows it off until then.
    [ObservableProperty]
    public partial bool IsAccountModeOn { get; private set; }
}
