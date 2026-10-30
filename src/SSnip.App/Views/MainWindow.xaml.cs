using System.ComponentModel;
using SSnip.App.Services;
using SSnip.App.ViewModels;
using SSnip.App.Views.Pages;
using SSnip.Core.Queue;

namespace SSnip.App.Views;

public partial class MainWindow
{
    private readonly JobQueue _queue;
    private bool _queueStopped;

    public MainWindow(MainWindowViewModel viewModel, PageProvider pageProvider, JobQueue queue)
    {
        _queue = queue;
        DataContext = viewModel;
        InitializeComponent();
        RootNavigation.SetPageProviderService(pageProvider);
        Loaded += (_, _) => RootNavigation.Navigate(typeof(HomePage));
        viewModel.Home.OpenQueueRequested += (_, _) => RootNavigation.Navigate(typeof(QueuePage));

        // "Fetch the link on the clipboard when the app gets focus" (off unless enabled in Settings).
        Activated += async (_, _) =>
        {
            await viewModel.Home.OnAppActivatedAsync();
        };
    }

    /// <summary>
    /// Stops running jobs before the window closes, so they are saved as queued and their yt-dlp
    /// and ffmpeg processes do not outlive the app.
    /// </summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!_queueStopped)
        {
            e.Cancel = true;
            IsEnabled = false;
            Title = "SSnip – saving the queue…";
            await _queue.ShutdownAsync();
            _queueStopped = true;
            Close();
            return;
        }

        base.OnClosing(e);
    }
}
