using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace SSnip.App.Views.Pages;

/// <summary>
/// An empty screen for the navigation shell. Each one is replaced by a real page in the phase it names.
/// </summary>
public abstract class PlaceholderPage : Page
{
    protected PlaceholderPage(string title, string purpose, string phase)
    {
        Title = title;

        // Pages in the navigation frame do not inherit the window's text colour.
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");

        var panel = new StackPanel { Margin = new Thickness(32, 24, 32, 24), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var description = new TextBlock { Text = purpose, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 24) };
        description.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        panel.Children.Add(description);

        var card = new Card
        {
            Content = new TextBlock { Text = $"Coming in {phase}.", TextWrapping = TextWrapping.Wrap },
        };
        panel.Children.Add(card);
        Content = panel;
    }
}

public sealed class SearchPage() : PlaceholderPage("Search", "Find footage inside the app and send results to a project, the queue or the clip editor.", "Phase 5");

public sealed class ClipEditorPage() : PlaceholderPage("Clip editor", "Scrub a preview proxy, mark in and out points, and download only those sections at full quality.", "Phase 3");

public sealed class LibraryPage() : PlaceholderPage("Library", "Browse every download and clip by project, search transcripts and export credits.", "Phase 4");

public sealed class ChannelsPage() : PlaceholderPage("Channels", "Watched channels and playlists that sync only new uploads.", "Phase 5");
