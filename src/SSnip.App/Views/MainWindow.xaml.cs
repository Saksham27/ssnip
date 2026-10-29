using SSnip.App.Services;
using SSnip.App.ViewModels;
using SSnip.App.Views.Pages;

namespace SSnip.App.Views;

public partial class MainWindow
{
    public MainWindow(MainWindowViewModel viewModel, PageProvider pageProvider)
    {
        DataContext = viewModel;
        InitializeComponent();
        RootNavigation.SetPageProviderService(pageProvider);
        Loaded += (_, _) => RootNavigation.Navigate(typeof(HomePage));

        // "Fetch the link on the clipboard when the app gets focus" (off unless enabled in Settings).
        Activated += async (_, _) =>
        {
            await viewModel.Home.OnAppActivatedAsync();
        };
    }
}
