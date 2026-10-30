using CommunityToolkit.Mvvm.ComponentModel;
using SSnip.Core.Clipping;

namespace SSnip.App.ViewModels;

/// <summary>One marked clip in the clip editor; name, tags and note are edited in place.</summary>
public sealed partial class ClipRowViewModel : ObservableObject
{
    public ClipRowViewModel(ClipMark clip)
    {
        In = clip.In;
        Out = clip.Out;
        Name = clip.Name;
        Tags = clip.Tags;
        Note = clip.Note;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InText), nameof(LengthText))]
    public partial TimeSpan In { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutText), nameof(LengthText))]
    public partial TimeSpan Out { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Tags { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    public string InText => ClipTime.Display(In);

    public string OutText => ClipTime.Display(Out);

    public string LengthText => ClipTime.Display(Out - In);

    public ClipMark ToMark() => new() { In = In, Out = Out, Name = Name, Tags = Tags, Note = Note };
}

public sealed record ProfileOption(EditorProfileKind Kind, string Name);

public sealed record CutModeOption(CutMode Mode, string Name);
