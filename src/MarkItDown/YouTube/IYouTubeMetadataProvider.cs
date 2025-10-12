namespace MarkItDown.YouTube;

/// <summary>
/// Provides access to YouTube video metadata and captions.
/// </summary>
public interface IYouTubeMetadataProvider
{
    Task<YouTubeMetadata?> GetVideoAsync(string videoId, CancellationToken cancellationToken = default);
}
