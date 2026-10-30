using SSnip.Core.Downloads;
using SSnip.Core.Queue;

namespace SSnip.Core.Clipping;

/// <summary>Everything a batch of clips shares.</summary>
/// <param name="Quality">The format preset for URL sources (Editor-safe, Max quality, 1080p cap).</param>
/// <param name="InfoJsonDirectory">Where info.json for the source log goes.</param>
public sealed record ClipBatch(
    string OutputDirectory,
    TimeSpan Handles,
    CutMode CutMode,
    EditorProfile Profile,
    FormatPreset Quality = FormatPreset.EditorSafe,
    string? InfoJsonDirectory = null);

/// <summary>Turns marked clips into queue jobs: section downloads for a URL, ffmpeg cuts for a local file.</summary>
public static class ClipJobBuilder
{
    public static IReadOnlyList<NewJob> ForUrl(string url, string? title, IReadOnlyList<ClipMark> clips, TimeSpan? duration, ClipBatch batch)
    {
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(batch);

        return [.. Named(clips).Select(c =>
        {
            var (start, end) = c.Clip.WithHandles(batch.Handles, duration);
            var options = new DownloadOptions
            {
                Url = url,
                OutputDirectory = batch.OutputDirectory,
                Preset = batch.Quality,
                FilenameTemplate = YtDlpArgsBuilder.ClipTemplate(c.Name),
                Sections = [ClipTime.Section(start, end)],
                CutMode = batch.CutMode,
                Profile = batch.Profile.Converts ? batch.Profile : null,
                InfoJsonDirectory = batch.InfoJsonDirectory,

                // Sections are always downloaded again, so there is nothing to skip or resume.
                Overwrite = true,
            };
            return new NewJob(options, string.IsNullOrWhiteSpace(title) ? c.Name : $"{c.Name} · {title}");
        })];
    }

    public static IReadOnlyList<NewJob> ForLocalFile(string path, IReadOnlyList<ClipMark> clips, TimeSpan? duration, ClipBatch batch)
    {
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(batch);

        return [.. Named(clips).Select(c =>
        {
            var (start, end) = c.Clip.WithHandles(batch.Handles, duration);
            return new NewJob(
                new LocalCutOptions
                {
                    InputPath = path,
                    OutputDirectory = batch.OutputDirectory,
                    Start = start,
                    End = end,
                    Name = c.Name,
                    Mode = batch.CutMode,
                    Profile = batch.Profile,
                },
                $"{c.Name} · {Path.GetFileName(path)}");
        })];
    }

    /// <summary>Valid clips with unique names: empty names become "Clip 3", repeats get " (2)".</summary>
    private static IEnumerable<(ClipMark Clip, string Name)> Named(IReadOnlyList<ClipMark> clips)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var clip in clips)
        {
            index++;
            if (!clip.IsValid)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(clip.Name) ? $"Clip {index}" : clip.Name.Trim();
            var unique = name;
            for (var n = 2; !used.Add(unique); n++)
            {
                unique = $"{name} ({n})";
            }

            yield return (clip, unique);
        }
    }
}
