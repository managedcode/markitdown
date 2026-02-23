using System.Globalization;
using System.Text;
using AngleSharp.Html.Parser;
using ManagedCode.MimeTypes;
using MarkItDown;
using MarkItDown.YouTube;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for YouTube and supported video-platform URLs.
/// URLs are resolved to downloadable media and delegated to the video conversion pipeline,
/// so transcription/analysis runs through configured media providers (for example Azure Video Indexer).
/// </summary>
public sealed class YouTubeUrlConverter : DocumentConverterBase
{
    private static readonly string[] mediaMimePrefixes = ["audio/", "video/"];
    private static readonly string[] videoMimePrefixes = ["video/"];
    private static readonly HashSet<string> SupportedVideoPlatformHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "vimeo.com",
        "www.vimeo.com",
        "player.vimeo.com",
        "dailymotion.com",
        "www.dailymotion.com",
        "dai.ly",
        "tiktok.com",
        "www.tiktok.com",
        "m.tiktok.com",
        "vm.tiktok.com",
        "twitch.tv",
        "www.twitch.tv",
        "clips.twitch.tv",
        "facebook.com",
        "www.facebook.com",
        "fb.watch",
        "instagram.com",
        "www.instagram.com",
        "x.com",
        "www.x.com",
        "twitter.com",
        "www.twitter.com",
        "rutube.ru",
        "www.rutube.ru",
        "bilibili.com",
        "www.bilibili.com",
    };

    private static readonly HashSet<string> VideoMetaKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "og:video",
        "og:video:url",
        "og:video:secure_url",
        "twitter:player:stream",
        "contentUrl",
        "contentURL"
    };

    private static readonly HttpClient sharedHttpClient = new();

    private readonly IYouTubeMetadataProvider metadataProvider;
    private readonly IYouTubeVideoDownloader youTubeVideoDownloader;
    private readonly DocumentConverterBase mediaConverter;
    private readonly HttpClient httpClient;

    public YouTubeUrlConverter(IYouTubeMetadataProvider? metadataProvider = null)
        : this(
            metadataProvider ?? new YoutubeExplodeMetadataProvider(),
            new YoutubeExplodeVideoDownloader(),
            new VideoConverter(),
            httpClient: null)
    {
    }

    internal YouTubeUrlConverter(
        IYouTubeMetadataProvider metadataProvider,
        IYouTubeVideoDownloader youTubeVideoDownloader,
        DocumentConverterBase mediaConverter,
        HttpClient? httpClient)
        : base(priority: 50)
    {
        this.metadataProvider = metadataProvider ?? throw new ArgumentNullException(nameof(metadataProvider));
        this.youTubeVideoDownloader = youTubeVideoDownloader ?? throw new ArgumentNullException(nameof(youTubeVideoDownloader));
        this.mediaConverter = mediaConverter ?? throw new ArgumentNullException(nameof(mediaConverter));
        this.httpClient = httpClient ?? sharedHttpClient;
    }

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var url = streamInfo.Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (streamInfo.MatchesMime(mediaMimePrefixes))
        {
            return false;
        }

        if (TryExtractVideoId(url, out _))
        {
            return true;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && IsSupportedVideoPlatformHost(uri.Host);
    }

    public override async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = streamInfo.Url;
            if (string.IsNullOrWhiteSpace(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var sourceUri))
            {
                throw new UnsupportedFormatException("Invalid video URL format");
            }

            var metadataTask = ResolveMetadataTask(url, cancellationToken);

            await using var media = await ResolveMediaAsync(stream, sourceUri, cancellationToken).ConfigureAwait(false);
            await using var mediaStream = File.OpenRead(media.FilePath);
            var mediaStreamInfo = media.StreamInfo.CopyWith(localPath: media.FilePath);
            var result = await mediaConverter.ConvertAsync(mediaStream, mediaStreamInfo, cancellationToken).ConfigureAwait(false);

            if (metadataTask is not null)
            {
                var metadata = await metadataTask.ConfigureAwait(false);
                if (metadata is not null)
                {
                    result = result.WithMetadata(BuildMetadataDictionary(metadata));
                }
            }

            return result;
        }
        catch (Exception ex) when (ex is not MarkItDownException)
        {
            throw new FileConversionException($"Failed to process video URL: {ex.Message}", ex);
        }
    }

    internal static bool TryExtractVideoId(string url, out string videoId)
    {
        videoId = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsYouTubeHost(uri.Host))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        string? candidate = null;

        if (host.EndsWith("youtu.be", StringComparison.Ordinal))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
            {
                candidate = segments[0];
            }
        }
        else
        {
            if (TryGetQueryParameter(uri, "v", out var queryVideoId))
            {
                candidate = queryVideoId;
            }
            else if (TryGetPathBasedVideoId(uri, out var pathVideoId))
            {
                candidate = pathVideoId;
            }
        }

        candidate = NormalizeVideoIdCandidate(candidate);
        if (string.IsNullOrWhiteSpace(candidate) || !YouTubeVideoId.IsMatch(candidate))
        {
            return false;
        }

        videoId = candidate;
        return true;
    }

    private static readonly System.Text.RegularExpressions.Regex YouTubeVideoId =
        new("^[a-zA-Z0-9_-]{11}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private Task<YouTubeMetadata?>? ResolveMetadataTask(string url, CancellationToken cancellationToken)
    {
        if (!TryExtractVideoId(url, out var videoId))
        {
            return null;
        }

        return TryGetMetadataAsync(videoId, cancellationToken);
    }

    private async Task<ResolvedVideoMedia> ResolveMediaAsync(Stream stream, Uri sourceUri, CancellationToken cancellationToken)
    {
        if (TryExtractVideoId(sourceUri.ToString(), out var videoId))
        {
            return await youTubeVideoDownloader.DownloadAsync(videoId, sourceUri, cancellationToken).ConfigureAwait(false);
        }

        if (!IsSupportedVideoPlatformHost(sourceUri.Host))
        {
            throw new UnsupportedFormatException($"Unsupported video URL host '{sourceUri.Host}'.");
        }

        var mediaUri = await ResolvePlatformMediaUrlAsync(stream, sourceUri, cancellationToken).ConfigureAwait(false);
        if (mediaUri is null)
        {
            throw new FileConversionException($"Could not resolve a downloadable media URL for '{sourceUri}'.");
        }

        return await DownloadVideoFromUriAsync(mediaUri, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Uri?> ResolvePlatformMediaUrlAsync(Stream stream, Uri sourceUri, CancellationToken cancellationToken)
    {
        var html = await TryReadHtmlAsync(stream, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(html))
        {
            using var response = await httpClient.GetAsync(sourceUri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        return await TryExtractVideoUriFromHtmlAsync(html, sourceUri, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ResolvedVideoMedia> DownloadVideoFromUriAsync(Uri mediaUri, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(mediaUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var mimeType = response.Content.Headers.ContentType?.MediaType;
        var extension = NormalizeExtension(Path.GetExtension(mediaUri.LocalPath));
        extension ??= GuessExtensionFromMime(mimeType);

        if (string.IsNullOrWhiteSpace(mimeType) && !string.IsNullOrWhiteSpace(extension))
        {
            mimeType = MimeHelper.GetMimeType(extension);
        }

        if (!StreamInfo.MatchesMime(mimeType, videoMimePrefixes))
        {
            throw new FileConversionException($"Resolved media URL '{mediaUri}' returned non-video content type '{mimeType ?? "unknown"}'.");
        }

        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new FileConversionException($"Could not infer file extension for resolved media URL '{mediaUri}'.");
        }

        await using var mediaStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var handle = await DiskBufferHandle.FromStreamAsync(
            mediaStream,
            extension,
            bufferSize: 1024 * 128,
            onChunkWritten: null,
            cancellationToken).ConfigureAwait(false);

        var fileName = Path.GetFileName(mediaUri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"video{extension}";
        }

        var mediaStreamInfo = new StreamInfo(
            mimeType: mimeType,
            extension: extension,
            fileName: fileName,
            localPath: handle.FilePath,
            url: mediaUri.ToString());

        return new ResolvedVideoMedia(handle.FilePath, mediaStreamInfo, handle);
    }

    private async Task<YouTubeMetadata?> TryGetMetadataAsync(string videoId, CancellationToken cancellationToken)
    {
        try
        {
            return await metadataProvider.GetVideoAsync(videoId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> TryReadHtmlAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (!stream.CanRead)
        {
            return null;
        }

        var originalPosition = stream.CanSeek ? stream.Position : 0;
        try
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var html = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(html) ? null : html;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = originalPosition;
            }
        }
    }

    private static async Task<Uri?> TryExtractVideoUriFromHtmlAsync(string html, Uri baseUri, CancellationToken cancellationToken)
    {
        var parser = new HtmlParser(new HtmlParserOptions
        {
            IsKeepingSourceReferences = false,
            IsEmbedded = false,
        });

        var document = await parser.ParseDocumentAsync(html, cancellationToken).ConfigureAwait(false);

        foreach (var meta in document.QuerySelectorAll("meta"))
        {
            var key = meta.GetAttribute("property")
                ?? meta.GetAttribute("name")
                ?? meta.GetAttribute("itemprop");

            if (string.IsNullOrWhiteSpace(key) || !VideoMetaKeys.Contains(key))
            {
                continue;
            }

            if (TryResolveHttpUri(baseUri, meta.GetAttribute("content"), out var resolved))
            {
                return resolved;
            }
        }

        foreach (var element in document.QuerySelectorAll("video[src], video source[src], link[as=video][href]"))
        {
            var value = element.GetAttribute("src") ?? element.GetAttribute("href");
            if (TryResolveHttpUri(baseUri, value, out var resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    private static bool TryResolveHttpUri(Uri baseUri, string? candidate, out Uri resolved)
    {
        resolved = baseUri;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUri, candidate.Trim(), out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        resolved = uri;
        return true;
    }

    private static bool IsSupportedVideoPlatformHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return SupportedVideoPlatformHosts.Contains(host.ToLowerInvariant());
    }

    private static bool IsYouTubeHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var normalizedHost = host.ToLowerInvariant();
        return normalizedHost is "youtube.com"
            or "www.youtube.com"
            or "m.youtube.com"
            or "music.youtube.com"
            or "youtu.be"
            or "www.youtu.be";
    }

    private static bool TryGetQueryParameter(Uri uri, string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(uri.Query))
        {
            return false;
        }

        var query = uri.Query;
        if (query.StartsWith("?", StringComparison.Ordinal))
        {
            query = query[1..];
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            string currentKey;
            string currentValue;
            if (separatorIndex < 0)
            {
                currentKey = Uri.UnescapeDataString(pair);
                currentValue = string.Empty;
            }
            else
            {
                currentKey = Uri.UnescapeDataString(pair[..separatorIndex]);
                currentValue = Uri.UnescapeDataString(pair[(separatorIndex + 1)..]);
            }

            if (string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
            {
                value = currentValue;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetPathBasedVideoId(Uri uri, out string videoId)
    {
        videoId = string.Empty;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        var first = segments[0].ToLowerInvariant();
        if (first is "shorts" or "embed" or "live")
        {
            videoId = segments[1];
            return true;
        }

        return false;
    }

    private static string NormalizeVideoIdCandidate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        var delimiterIndex = normalized.IndexOfAny(['?', '&', '#', '/']);
        if (delimiterIndex >= 0)
        {
            normalized = normalized[..delimiterIndex];
        }

        return normalized.Trim();
    }

    private static Dictionary<string, string> BuildMetadataDictionary(YouTubeMetadata metadata)
    {
        var values = new Dictionary<string, string>(metadata.AdditionalMetadata, StringComparer.OrdinalIgnoreCase)
        {
            [MetadataKeys.VideoId] = metadata.VideoId,
            [MetadataKeys.Channel] = metadata.ChannelTitle,
            [MetadataKeys.ChannelUrl] = metadata.ChannelUrl.ToString(),
            [MetadataKeys.Provider] = MetadataValues.ProviderYouTube
        };

        if (metadata.Duration.HasValue)
        {
            values[MetadataKeys.Duration] = metadata.Duration.Value.ToString();
        }

        if (metadata.UploadDate.HasValue)
        {
            values[MetadataKeys.UploadDate] = metadata.UploadDate.Value.ToString("u", CultureInfo.InvariantCulture);
        }

        if (metadata.ViewCount.HasValue)
        {
            values[MetadataKeys.ViewCount] = metadata.ViewCount.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (metadata.LikeCount.HasValue)
        {
            values[MetadataKeys.LikeCount] = metadata.LikeCount.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (metadata.Tags.Count > 0)
        {
            values[MetadataKeys.Tags] = string.Join(",", metadata.Tags);
        }

        return values;
    }

    private static string? NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        return extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
    }

    private static string? GuessExtensionFromMime(string? mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
        {
            return null;
        }

        if (!MimeHelper.TryGetExtensions(mime, out var extensions) || extensions.Count == 0)
        {
            return null;
        }

        var extension = extensions.FirstOrDefault();
        return NormalizeExtension(extension);
    }
}
