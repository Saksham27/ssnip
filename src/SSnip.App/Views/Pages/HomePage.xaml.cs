using System.Windows;
using System.Windows.Threading;
using SSnip.App.ViewModels;

namespace SSnip.App.Views.Pages;

public partial class HomePage
{
    private readonly HomeViewModel _viewModel;

    public HomePage(HomeViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // Pasting into the link box fetches straight away, like Ctrl+V elsewhere on the page.
        DataObject.AddPastingHandler(LinkBox, (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _viewModel.FetchCommand.Execute(null)));

        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    private static string? DroppedText(IDataObject data) =>
        data.GetDataPresent(DataFormats.UnicodeText) ? data.GetData(DataFormats.UnicodeText) as string
        : data.GetDataPresent("UniformResourceLocatorW") ? ReadUrl(data, "UniformResourceLocatorW", System.Text.Encoding.Unicode)
        : data.GetDataPresent(DataFormats.Text) ? data.GetData(DataFormats.Text) as string
        : null;

    private static string? ReadUrl(IDataObject data, string format, System.Text.Encoding encoding) =>
        data.GetData(format) is System.IO.MemoryStream stream ? encoding.GetString(stream.ToArray()).TrimEnd('\0') : null;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedText(e.Data) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DroppedText(e.Data) is { } text)
        {
            e.Handled = true;
            await _viewModel.OnLinksDroppedAsync(text);
        }
    }
}
