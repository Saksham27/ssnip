namespace SSnip.Core.Clipping;

/// <summary>
/// Keeps the clips marked on a source (a video URL or a local file path), so closing the clip
/// editor or the app does not lose them. Phase 4 links them to projects and assets.
/// </summary>
public interface IClipStore
{
    Task<IReadOnlyList<ClipMark>> LoadAsync(string source, CancellationToken cancellationToken = default);

    /// <summary>Replaces the clips stored for <paramref name="source"/>, keeping their order.</summary>
    Task SaveAsync(string source, IReadOnlyList<ClipMark> clips, CancellationToken cancellationToken = default);
}
