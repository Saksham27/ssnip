namespace SSnip.Core.Tools;

/// <summary>What the dependency manager found for one tool.</summary>
public sealed record ToolStatus(ExternalTool Tool, string Path, bool IsInstalled, string? Version, string? Error)
{
    public bool IsHealthy => IsInstalled && Version is not null && Error is null;
}

public enum InstallStage
{
    Downloading,
    Extracting,
    Updating,
    Verifying,
}

/// <summary>Progress of a tool download or update. <see cref="TotalBytes"/> is null when the server sends no length.</summary>
public sealed record InstallProgress(InstallStage Stage, long BytesReceived = 0, long? TotalBytes = null)
{
    public double? Fraction => TotalBytes is > 0 ? (double)BytesReceived / TotalBytes.Value : null;
}
