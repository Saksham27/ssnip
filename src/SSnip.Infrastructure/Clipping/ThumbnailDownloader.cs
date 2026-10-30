using SSnip.Core.Clipping;

namespace SSnip.Infrastructure.Clipping;

/// <summary>Saves a source's best thumbnail (the URL yt-dlp reported) for reference.</summary>
public interface IThumbnailDownloader
{
    Task<string> SaveAsync(string thumbnailUrl, string folder, string name, CancellationToken cancellationToken = default);
}

public sealed class ThumbnailDownloader(HttpClient http) : IThumbnailDownloader
{
    public async Task<string> SaveAsync(string thumbnailUrl, string folder, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailUrl);
        Directory.CreateDirectory(folder);

        using var response = await http.GetAsync(new Uri(thumbnailUrl), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var extension = response.Content.Headers.ContentType?.MediaType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg",
        };

        var path = Path.Combine(folder, $"{FileNames.Safe(name)} thumbnail{extension}");
        await using var file = File.Create(path);
        await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        return path;
    }
}
