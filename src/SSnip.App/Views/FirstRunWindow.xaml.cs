using SSnip.App.ViewModels;

namespace SSnip.App.Views;

public partial class FirstRunWindow
{
    public FirstRunWindow(FirstRunViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        viewModel.Completed += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
    }
}
