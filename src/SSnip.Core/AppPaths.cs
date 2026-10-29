using SSnip.Core.Tools;

namespace SSnip.Core;

/// <summary>
/// Locations of SSnip's runtime data. Everything lives under one root,
/// <c>%LOCALAPPDATA%\SSnip</c> by default.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    public static AppPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SSnip"));

    public string Root { get; }

    public string Bin => Path.Combine(Root, "bin");

    public string Data => Path.Combine(Root, "data");

    public string Logs => Path.Combine(Root, "logs");

    public string Cache => Path.Combine(Root, "cache");

    public string Downloads => Path.Combine(Cache, "downloads");

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string DatabaseFile => Path.Combine(Data, "footage.db");

    public IReadOnlyList<string> Directories => [Root, Bin, Data, Logs, Cache, Downloads];

    public string ToolPath(ExternalTool tool) => Path.Combine(Bin, ToolDescriptor.For(tool).ExecutableName);
}
