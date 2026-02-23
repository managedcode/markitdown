using ManagedCode.MimeTypes;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace MarkItDown.YouTube;

internal sealed class YoutubeExplodeVideoDownloader : IYouTubeVideoDownloader
{
    private static readonly string[] videoMimePrefixes = ["video/"];

    private readonly YoutubeClient client;

    public YoutubeExplodeVideoDownloader(YoutubeClient? client = null)
    {
        this.client = client ?? new YoutubeClient();
    }

    public async Task<ResolvedVideoMedia> DownloadAsync(string videoId, Uri sourceUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            throw new FileConversionException("YouTube video identifier is required to download media.");
        }

        var parsedVideoId = VideoId.TryParse(videoId);
        if (!parsedVideoId.HasValue)
        {
            throw new FileConversionException($"Invalid YouTube video identifier '{videoId}'.");
        }

        var manifest = await client.Videos.Streams.GetManifestAsync(parsedVideoId.Value, cancellationToken).ConfigureAwait(false);
        var muxedStream = manifest.GetMuxedStreams().TryGetWithHighestVideoQuality()
            ?? manifest.GetMuxedStreams().TryGetWithHighestBitrate();

        if (muxedStream is null)
        {
            throw new FileConversionException($"No downloadable muxed streams were found for YouTube video '{videoId}'.");
        }

        var extension = NormalizeExtension(muxedStream.Container.Name);
        var mimeType = MimeHelper.GetMimeType(extension);
        if (!StreamInfo.MatchesMime(mimeType, videoMimePrefixes))
        {
            throw new FileConversionException($"Resolved YouTube stream container '{muxedStream.Container.Name}' is not a supported video MIME.");
        }

        await using var sourceStream = await client.Videos.Streams.GetAsync(muxedStream, cancellationToken).ConfigureAwait(false);
        var buffer = await DiskBufferHandle.FromStreamAsync(
            sourceStream,
            extension,
            bufferSize: 1024 * 128,
            onChunkWritten: null,
            cancellationToken).ConfigureAwait(false);
        var resolvedMediaUrl = muxedStream.Url.ToString();
        if (string.IsNullOrWhiteSpace(resolvedMediaUrl))
        {
            resolvedMediaUrl = sourceUrl.ToString();
        }

        var streamInfo = new StreamInfo(
            mimeType: mimeType,
            extension: extension,
            fileName: $"{videoId}{extension}",
            localPath: buffer.FilePath,
            url: resolvedMediaUrl);

        return new ResolvedVideoMedia(buffer.FilePath, streamInfo, buffer);
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new FileConversionException("Resolved YouTube stream container did not provide a file extension.");
        }

        return extension.StartsWith('.')
            ? extension.ToLowerInvariant()
            : "." + extension.ToLowerInvariant();
    }
}
