using SSnip.Core.Downloads;

namespace SSnip.Core.Tools;

/// <summary>
/// The first-run self-check's test download: 5 seconds of a 360p proxy from the owner's own
/// Jemma Live clip, so no third-party content is ever used.
/// </summary>
public static class SelfCheck
{
    public const string TestVideoUrl = "https://www.youtube.com/watch?v=BwiFiiPk_1s";

    public static DownloadOptions TestDownload(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return new DownloadOptions
        {
            Url = TestVideoUrl,
            OutputDirectory = Path.Combine(paths.Cache, "selfcheck"),
            FilenameTemplate = "selfcheck.%(ext)s",
            Preset = FormatPreset.PreviewProxy,
            Sections = ["*0-5"],
            Overwrite = true,
        };
    }
}
