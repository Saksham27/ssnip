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
    }
}
