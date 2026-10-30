using CommunityToolkit.Mvvm.ComponentModel;
using SSnip.Core.Downloads;
using SSnip.Core.Formatting;
using SSnip.Core.Media;

namespace SSnip.App.ViewModels;

/// <summary>One video in a playlist or channel listing, with a checkbox.</summary>
public sealed partial class PlaylistEntryViewModel(int index, PlaylistEntry entry) : ObservableObject
{
    /// <summary>1-based position in the listing, as used by item lists like <c>1:10</c>.</summary>
    public int Index { get; } = index;

    public PlaylistEntry Entry { get; } = entry;

    public string Title => Entry.Title;

    public string DurationText => Display.Duration(Entry.Duration);

    /// <summary>The link to download; flat listings sometimes give only the id.</summary>
    public string Url => Entry.Url ?? Entry.Id;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = true;
}

public enum SponsorBlockAction
{
    Keep,
    Mark,
    Remove,
}

/// <summary>One SponsorBlock category: keep it, mark it as a chapter or cut it out.</summary>
public sealed partial class SponsorBlockCategoryViewModel(string category, string name, SponsorBlockAction action) : ObservableObject
{
    public static IReadOnlyList<SponsorBlockAction> Actions { get; } = Enum.GetValues<SponsorBlockAction>();

    public string Category { get; } = category;

    public string Name { get; } = name;

    [ObservableProperty]
    public partial SponsorBlockAction Action { get; set; } = action;

    public static IReadOnlyList<SponsorBlockCategoryViewModel> FootageDefaults() =>
    [
        .. SponsorBlockOptions.Categories.Select(c => new SponsorBlockCategoryViewModel(
            c,
            c switch
            {
                "selfpromo" => "Self-promotion",
                "interaction" => "Interaction reminder",
                "music_offtopic" => "Non-music section",
                "preview" => "Preview / recap",
                _ => char.ToUpperInvariant(c[0]) + c[1..],
            },
            SponsorBlockOptions.FootageDefault.Remove.Contains(c) ? SponsorBlockAction.Remove : SponsorBlockAction.Mark)),
    ];
}

public sealed record SubtitleModeOption(SubtitleMode Mode, string Name);

public sealed record ChannelTabOption(ChannelTab Tab, string Name);
