namespace SSnip.Core.Tools;

/// <summary>Finds, downloads and updates the external tools in the app's bin folder.</summary>
public interface IDependencyManager
{
    Task<ToolStatus> GetStatusAsync(ExternalTool tool, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ToolStatus>> GetAllStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the latest build of a tool. FFmpeg and ffprobe ship in one archive,
    /// so installing either one installs both.
    /// </summary>
    Task InstallAsync(ExternalTool tool, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Updates a tool in place: <c>yt-dlp -U</c> for yt-dlp, a fresh download for the others.</summary>
    Task UpdateAsync(ExternalTool tool, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Copies an existing copy of the tool's executable into the bin folder.</summary>
    Task ImportAsync(ExternalTool tool, string sourceExecutablePath, CancellationToken cancellationToken = default);

    /// <summary>Asks yt-dlp which JavaScript runtimes it will use. Empty means none; YouTube downloads need one.</summary>
    Task<IReadOnlyList<string>> GetJavaScriptRuntimesAsync(CancellationToken cancellationToken = default);
}
