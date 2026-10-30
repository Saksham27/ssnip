using SSnip.App.ViewModels;

namespace SSnip.App.Views.Pages;

public partial class QueuePage
{
    public QueuePage(QueueViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
