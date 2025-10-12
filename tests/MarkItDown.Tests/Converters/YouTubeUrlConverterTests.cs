using System.Collections.Generic;
using System.Text;
using MarkItDown.Converters;
using MarkItDown.Intelligence.Models;
using MarkItDown.YouTube;
using Shouldly;
using Xunit;

namespace MarkItDown.Tests.Converters;

public class YouTubeUrlConverterTests
{
    private const string SampleUrl = "https://www.youtube.com/watch?v=abcdefghijk";

    [Fact]
    public async Task ConvertAsync_ProviderReturnsMetadata_EmitsSegments()
    {
        var provider = new StubYouTubeMetadataProvider();
        var converter = new YouTubeUrlConverter(provider);
        var streamInfo = new StreamInfo(url: SampleUrl);

        var result = await converter.ConvertAsync(Stream.Null, streamInfo);

        result.Markdown.ShouldContain("Sample Video Title");
        result.Segments.Count.ShouldBeGreaterThan(0);
        result.Segments.ShouldContain(segment => segment.Type == SegmentType.Metadata);
        result.Segments.ShouldContain(segment => segment.Type == SegmentType.Audio && segment.Markdown.Contains("Hello captions"));
    }

    [Fact]
    public async Task ConvertAsync_ProviderReturnsNull_FallsBackToBasicMetadata()
    {
        var provider = new NullYouTubeMetadataProvider();
        var converter = new YouTubeUrlConverter(provider);
        var streamInfo = new StreamInfo(url: SampleUrl);

        var result = await converter.ConvertAsync(Stream.Null, streamInfo);

        result.Markdown.ShouldContain("YouTube Video");
        result.Markdown.ShouldContain("Video ID");
        result.Markdown.ShouldContain("Video URL");
        result.Segments.Count.ShouldBe(0);
    }

    private sealed class NullYouTubeMetadataProvider : IYouTubeMetadataProvider
    {
        public Task<YouTubeMetadata?> GetVideoAsync(string videoId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<YouTubeMetadata?>(null);
        }
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
                Tags: new[] { "test", "video" },
                Description: "Sample description",
                Thumbnails: new[] { new Uri($"https://img.youtube.com/vi/{videoId}/0.jpg") },
                Captions: new[]
                {
                    new YouTubeCaptionSegment("Hello captions", TimeSpan.Zero, TimeSpan.FromSeconds(5), new Dictionary<string, string>
                    {
                        [MetadataKeys.Language] = "en",
                        [MetadataKeys.Provider] = MetadataValues.ProviderYouTube
                    })
                },
                AdditionalMetadata: new Dictionary<string, string>()
            );

            return Task.FromResult<YouTubeMetadata?>(metadata);
        }
    }
}
