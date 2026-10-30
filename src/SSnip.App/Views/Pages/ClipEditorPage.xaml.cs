using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SSnip.App.ViewModels;

namespace SSnip.App.Views.Pages;

public partial class ClipEditorPage
{
    private readonly ClipEditorViewModel _viewModel;

    public ClipEditorPage(ClipEditorViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            Video.MediaPlayer ??= viewModel.Player;
            viewModel.OnVideoViewAttached();
            Focus();
        };
        Unloaded += async (_, _) =>
        {
            viewModel.PauseCommand.Execute(null);
            await viewModel.SaveClipsAsync();
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>The editor keys from the plan. Typing in a text box (clip names, timestamps) is left alone.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase or ComboBox || !_viewModel.HasSource)
        {
            return;
        }

        var command = (e.Key, Keyboard.Modifiers) switch
        {
            (Key.I, ModifierKeys.None) => _viewModel.SetInCommand,
            (Key.O, ModifierKeys.None) => _viewModel.SetOutCommand,
            (Key.J, ModifierKeys.None) => _viewModel.ReverseCommand,
            (Key.K, ModifierKeys.None) or (Key.Space, ModifierKeys.None) => _viewModel.PlayPauseCommand,
            (Key.L, ModifierKeys.None) => _viewModel.ForwardCommand,
            (Key.Left, ModifierKeys.None) => _viewModel.StepBackCommand,
            (Key.Right, ModifierKeys.None) => _viewModel.StepForwardCommand,
            (Key.Enter, ModifierKeys.None) => _viewModel.AddClipCommand,
            (Key.Z, ModifierKeys.Control) => _viewModel.UndoCommand,
            _ => null,
        };

        if (command is not null)
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }

            e.Handled = true;
        }
    }
}
