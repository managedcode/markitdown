using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using MarkItDown.Converters;
using MarkItDown.YouTube;
using Shouldly;
using Xunit;

namespace MarkItDown.Tests.Converters;

public class YouTubeUrlConverterTests
{
    private const string YouTubeUrl = "https://www.youtube.com/watch?v=abcdefghijk";

    [Fact]
    public void AcceptsInput_YouTubeAndSupportedPlatform_ReturnsTrue()
    {
        var converter = CreateConverter();

        converter.AcceptsInput(new StreamInfo(url: YouTubeUrl)).ShouldBeTrue();
        converter.AcceptsInput(new StreamInfo(url: "https://vimeo.com/1234567")).ShouldBeTrue();
    }

    [Fact]
    public void AcceptsInput_UrlWithVideoMime_ReturnsFalse()
    {
        var converter = CreateConverter();
        var streamInfo = new StreamInfo(
            mimeType: "video/mp4",
            extension: ".mp4",
            fileName: "upload.mp4",
            url: YouTubeUrl);

        converter.AcceptsInput(streamInfo).ShouldBeFalse();
    }

    [Fact]
    public async Task ConvertAsync_YouTubeUrl_DownloadsVideoAndDelegatesToMediaConverter()
    {
        var metadataProvider = new StubYouTubeMetadataProvider();
        var downloader = new StubYouTubeVideoDownloader();
        var mediaConverter = new SpyMediaConverter();
        var converter = new YouTubeUrlConverter(metadataProvider, downloader, mediaConverter, httpClient: null);

        var result = await converter.ConvertAsync(Stream.Null, new StreamInfo(url: YouTubeUrl));

        downloader.CallCount.ShouldBe(1);
        downloader.LastVideoId.ShouldBe("abcdefghijk");
        mediaConverter.CallCount.ShouldBe(1);
        mediaConverter.LastStreamInfo.ShouldNotBeNull();
        mediaConverter.LastStreamInfo!.MimeType.ShouldBe("video/mp4");
        mediaConverter.LastStreamInfo.Url.ShouldBe("https://youtube-media.example.com/abcdefghijk.mp4?sig=abc");
        result.Markdown.ShouldBe("## Media transcript");
        result.Metadata.TryGetValue(MetadataKeys.Provider, out var provider).ShouldBeTrue();
        provider.ShouldBe(MetadataValues.ProviderYouTube);
        result.Metadata.TryGetValue(MetadataKeys.VideoId, out var videoId).ShouldBeTrue();
        videoId.ShouldBe("abcdefghijk");
    }

    [Fact]
    public async Task ConvertAsync_SupportedNonYouTubeUrl_ResolvesVideoFromHtmlAndDelegatesToMediaConverter()
    {
        var metadataProvider = new StubYouTubeMetadataProvider();
        var downloader = new StubYouTubeVideoDownloader();
        var mediaConverter = new SpyMediaConverter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            request.RequestUri.ShouldNotBeNull();
            request.RequestUri!.ToString().ShouldBe("https://cdn.example.com/video.mp4");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4, 5])
                {
                    Headers =
                    {
                        ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4")
                    }
                }
            };
        }));

        var converter = new YouTubeUrlConverter(metadataProvider, downloader, mediaConverter, httpClient);
        const string html = "<html><head><meta property=\"og:video\" content=\"https://cdn.example.com/video.mp4\"/></head><body></body></html>";
        using var htmlStream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var result = await converter.ConvertAsync(htmlStream, new StreamInfo(url: "https://vimeo.com/1234567"));

        downloader.CallCount.ShouldBe(0);
        mediaConverter.CallCount.ShouldBe(1);
        mediaConverter.LastStreamInfo.ShouldNotBeNull();
        mediaConverter.LastStreamInfo!.MimeType.ShouldBe("video/mp4");
        mediaConverter.LastStreamInfo.Url.ShouldBe("https://cdn.example.com/video.mp4");
        result.Markdown.ShouldBe("## Media transcript");
    }

    [Fact]
    public async Task ConvertAsync_SupportedNonYouTubeUrl_WhenResolvedContentIsNotVideo_ThrowsFileConversionException()
    {
        var metadataProvider = new StubYouTubeMetadataProvider();
        var downloader = new StubYouTubeVideoDownloader();
        var mediaConverter = new SpyMediaConverter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not a video", Encoding.UTF8, "text/html")
        }));

        var converter = new YouTubeUrlConverter(metadataProvider, downloader, mediaConverter, httpClient);
        const string html = "<html><head><meta property=\"og:video\" content=\"https://cdn.example.com/not-video\"/></head><body></body></html>";
        using var htmlStream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var exception = await Should.ThrowAsync<FileConversionException>(
            () => converter.ConvertAsync(htmlStream, new StreamInfo(url: "https://vimeo.com/1234567")));

        exception.Message.ShouldContain("non-video content type");
        mediaConverter.CallCount.ShouldBe(0);
    }

    private static YouTubeUrlConverter CreateConverter()
    {
        return new YouTubeUrlConverter(
            new StubYouTubeMetadataProvider(),
            new StubYouTubeVideoDownloader(),
            new SpyMediaConverter(),
            httpClient: null);
    }

    private sealed class StubYouTubeMetadataProvider : IYouTubeMetadataProvider
    {
        public Task<YouTubeMetadata?> GetVideoAsync(string videoId, CancellationToken cancellationToken = default)
        {
            var metadata = new YouTubeMetadata(
                VideoId: videoId,
                Title: "Sample Video Title",
                ChannelTitle: "Sample Channel",
                WatchUrl: new Uri($"https://youtu.be/{videoId}"),
                ChannelUrl: new Uri("https://youtube.com/channel/sample"),
                Duration: TimeSpan.FromMinutes(3),
                UploadDate: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ViewCount: 12345,
                LikeCount: 678,
                Tags: ["test", "video"],
                Description: "Sample description",
                Thumbnails: [new Uri($"https://img.youtube.com/vi/{videoId}/0.jpg")],
                Captions: Array.Empty<YouTubeCaptionSegment>(),
                AdditionalMetadata: new Dictionary<string, string>());

            return Task.FromResult<YouTubeMetadata?>(metadata);
        }
    }

    private sealed class StubYouTubeVideoDownloader : IYouTubeVideoDownloader
    {
        public int CallCount { get; private set; }
        public string? LastVideoId { get; private set; }

        public async Task<ResolvedVideoMedia> DownloadAsync(string videoId, Uri sourceUrl, CancellationToken cancellationToken = default)
        {
            _ = sourceUrl;
            CallCount++;
            LastVideoId = videoId;

            var path = Path.Combine(Path.GetTempPath(), $"markitdown-{Guid.NewGuid():N}.mp4");
            await File.WriteAllBytesAsync(path, [0, 1, 2, 3], cancellationToken);

            var streamInfo = new StreamInfo(
                mimeType: "video/mp4",
                extension: ".mp4",
                fileName: "downloaded.mp4",
                localPath: path,
                url: $"https://youtube-media.example.com/{videoId}.mp4?sig=abc");

            return new ResolvedVideoMedia(path, streamInfo, new FileCleanup(path));
        }
    }

    private sealed class SpyMediaConverter : DocumentConverterBase
    {
        public SpyMediaConverter()
            : base(priority: 1)
        {
        }

        public int CallCount { get; private set; }

        public StreamInfo? LastStreamInfo { get; private set; }

        public override bool AcceptsInput(StreamInfo streamInfo)
        {
            return true;
        }

        public override Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastStreamInfo = streamInfo;
            return Task.FromResult(new DocumentConverterResult("## Media transcript", "Media"));
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }

    private sealed class FileCleanup(string path) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }

            return ValueTask.CompletedTask;
        }
    }
}
