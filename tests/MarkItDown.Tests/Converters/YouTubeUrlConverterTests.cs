using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MarkItDown.Converters;
using MarkItDown.Intelligence.Models;
using MarkItDown.YouTube;
using Shouldly;
using Xunit;
using MarkItDown.Tests;

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

    [Fact]
    public async Task ConvertAsync_WatchUrlWithAdditionalQueryParams_ExtractsVideoId()
    {
        var provider = new StubYouTubeMetadataProvider();
        var converter = new YouTubeUrlConverter(provider);
        var streamInfo = new StreamInfo(url: "https://www.youtube.com/watch?si=abc123&v=abcdefghijk&feature=share");

        var result = await converter.ConvertAsync(Stream.Null, streamInfo);

        var metadataSegment = result.Segments.First(segment => segment.Type == SegmentType.Metadata);
        metadataSegment.AdditionalMetadata[MetadataKeys.VideoId].ShouldBe("abcdefghijk");
    }

    private sealed class NullYouTubeMetadataProvider : IYouTubeMetadataProvider
    {
        public Task<YouTubeMetadata?> GetVideoAsync(string videoId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<YouTubeMetadata?>(null);
        }
    }

    [Fact]
    public async Task ConvertAsync_WithRecordedMetadata_RendersVideoDetails()
    {
        var metadata = LoadRecordedMetadata();
        var provider = new FixtureYouTubeMetadataProvider(metadata);
        var converter = new YouTubeUrlConverter(provider);
        var streamInfo = new StreamInfo(url: "https://www.youtube.com/watch?v=8hnpIIamb6k");

        var result = await converter.ConvertAsync(Stream.Null, streamInfo);

        result.Title.ShouldBe(metadata.Title);
        result.Markdown.ShouldContain(metadata.Title);
        result.Markdown.ShouldContain("Managed Code");
        result.Markdown.ShouldContain("**Views:** 484");
        result.Markdown.ShouldContain("SOLID Principles");
        result.Markdown.ShouldContain("## Captions");
        result.Segments.ShouldContain(segment => segment.Type == SegmentType.Metadata);
        result.Segments.Count(s => s.Type == SegmentType.Audio).ShouldBe(metadata.Captions.Count);

        var firstCaption = result.Segments.First(s => s.Type == SegmentType.Audio);
        firstCaption.StartTime.ShouldNotBeNull();
        firstCaption.StartTime.Value.ShouldBeGreaterThan(TimeSpan.Zero);
        result.Segments.ShouldContain(segment =>
            segment.Type == SegmentType.Audio &&
            segment.Markdown.Contains("principles", StringComparison.OrdinalIgnoreCase));
    }

    private static YouTubeMetadata LoadRecordedMetadata()
    {
        var jsonPath = TestAssetLoader.GetAssetPath(TestAssetCatalog.YoutubeSolidPrinciplesJson);
        using var stream = File.OpenRead(jsonPath);
        var fixture = JsonSerializer.Deserialize<YouTubeMetadataFixture>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (fixture is null)
        {
            throw new InvalidOperationException("Failed to deserialize recorded YouTube metadata fixture.");
        }

        var captions = fixture.Captions.Select(c => new YouTubeCaptionSegment(
            c.Text,
            c.Start is not null ? TimeSpan.FromSeconds(c.Start.Value) : null,
            c.End is not null ? TimeSpan.FromSeconds(c.End.Value) : null,
            c.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        )).ToList();

        var thumbnails = fixture.Thumbnails.Select(uri => new Uri(uri)).ToList();
        var additional = fixture.AdditionalMetadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new YouTubeMetadata(
            VideoId: fixture.VideoId,
            Title: fixture.Title,
            ChannelTitle: fixture.ChannelTitle,
            WatchUrl: new Uri(fixture.WatchUrl),
            ChannelUrl: new Uri(fixture.ChannelUrl),
            Duration: fixture.DurationSeconds is not null ? TimeSpan.FromSeconds(fixture.DurationSeconds.Value) : null,
            UploadDate: fixture.UploadDate is not null ? DateTimeOffset.Parse(fixture.UploadDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) : null,
            ViewCount: fixture.ViewCount,
            LikeCount: fixture.LikeCount,
            Tags: fixture.Tags ?? Array.Empty<string>(),
            Description: fixture.Description,
            Thumbnails: thumbnails,
            Captions: captions,
            AdditionalMetadata: additional
        );
    }

    private sealed class StubYouTubeMetadataProvider : IYouTubeMetadataProvider
    {
        private static readonly string[] Tags = new[] { "test", "video" };

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
                Tags: Tags,
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

    private sealed class FixtureYouTubeMetadataProvider : IYouTubeMetadataProvider
    {
        private readonly YouTubeMetadata metadata;

        public FixtureYouTubeMetadataProvider(YouTubeMetadata metadata)
        {
            this.metadata = metadata;
        }

        public Task<YouTubeMetadata?> GetVideoAsync(string videoId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<YouTubeMetadata?>(metadata);
        }
    }

    private sealed class YouTubeMetadataFixture
    {
        public string VideoId { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string ChannelTitle { get; init; } = string.Empty;
        public string WatchUrl { get; init; } = string.Empty;
        public string ChannelUrl { get; init; } = string.Empty;
        public double? DurationSeconds { get; init; }
        public string? UploadDate { get; init; }
        public long? ViewCount { get; init; }
        public long? LikeCount { get; init; }
        public IReadOnlyList<string>? Tags { get; init; }
        public string? Description { get; init; }
        public IReadOnlyList<string> Thumbnails { get; init; } = Array.Empty<string>();
        public IReadOnlyList<YouTubeCaptionFixture> Captions { get; init; } = Array.Empty<YouTubeCaptionFixture>();
        public IReadOnlyDictionary<string, string>? AdditionalMetadata { get; init; }
    }

    private sealed class YouTubeCaptionFixture
    {
        public string Text { get; init; } = string.Empty;
        public double? Start { get; init; }
        public double? End { get; init; }
        public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    }
}
