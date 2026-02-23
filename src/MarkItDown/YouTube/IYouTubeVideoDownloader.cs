namespace MarkItDown.YouTube;

internal interface IYouTubeVideoDownloader
{
    Task<ResolvedVideoMedia> DownloadAsync(string videoId, Uri sourceUrl, CancellationToken cancellationToken = default);
}
