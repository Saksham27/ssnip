using CommunityToolkit.Mvvm.ComponentModel;
using SSnip.Core.Tools;

namespace SSnip.App.ViewModels;

public enum ToolRowState
{
    Checking,
    Missing,
    Ready,
    Broken,
    Working,
}

/// <summary>One tool in the dependency manager list.</summary>
public sealed partial class ToolRowViewModel(ExternalTool tool, string path) : ObservableObject
{
    public ExternalTool Tool { get; } = tool;

    public string DisplayName { get; } = ToolDescriptor.For(tool).DisplayName;

    [ObservableProperty]
    public partial string Path { get; set; } = path;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHealthy), nameof(IsWorking))]
    public partial ToolRowState State { get; set; } = ToolRowState.Checking;

    [ObservableProperty]
    public partial string? Version { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Checking…";

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; set; } = true;

    public bool IsHealthy => State == ToolRowState.Ready;

    public bool IsWorking => State is ToolRowState.Working or ToolRowState.Checking;

    public void Apply(ToolStatus status)
    {
        Path = status.Path;
        Version = status.Version;
        (State, StatusText) = status switch
        {
            { IsHealthy: true } => (ToolRowState.Ready, status.Version!),
            { IsInstalled: false } => (ToolRowState.Missing, "Not installed"),
            _ => (ToolRowState.Broken, status.Error ?? "Does not run"),
        };
    }

    public void Report(InstallProgress progress)
    {
        State = ToolRowState.Working;
        IsProgressIndeterminate = progress.Fraction is null;
        Progress = (progress.Fraction ?? 0) * 100;
        StatusText = progress.Stage switch
        {
            InstallStage.Downloading when progress.TotalBytes is > 0 =>
                $"Downloading {FormatMegabytes(progress.BytesReceived)} of {FormatMegabytes(progress.TotalBytes.Value)}",
            InstallStage.Downloading => $"Downloading {FormatMegabytes(progress.BytesReceived)}",
            InstallStage.Extracting => "Extracting…",
            InstallStage.Updating => "Updating…",
            InstallStage.Verifying => "Checking version…",
            _ => StatusText,
        };
    }

    public void Fail(string message)
    {
        State = ToolRowState.Broken;
        StatusText = message;
    }

    private static string FormatMegabytes(long bytes) => $"{bytes / 1024d / 1024d:0.0} MB";
}
