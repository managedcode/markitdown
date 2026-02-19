using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;
using MarkItDown.Converters;
using MarkItDown.Intelligence.Models;
using Shouldly;
using Xunit;
using Xunit.Sdk;
using YoutubeExplode.Exceptions;

namespace MarkItDown.Tests.Converters;

/// <summary>
/// Live integration tests that exercise the YouTube converter against the public API.
/// These tests rely on network connectivity and will be skipped automatically when YouTube
/// metadata cannot be retrieved (for example, when the network is unavailable or rate limited).
/// </summary>
public sealed class YouTubeUrlConverterLiveTests
{
    private const string SolidPrinciplesVideoUrl = "https://www.youtube.com/watch?v=8hnpIIamb6k";

    [Fact]
    public async Task ConvertAsync_WithLiveVideo_FetchesMetadataFromYouTube()
    {
        var converter = new YouTubeUrlConverter();
        var streamInfo = new StreamInfo(url: SolidPrinciplesVideoUrl);

        DocumentConverterResult result;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        try
        {
            result = await converter.ConvertAsync(Stream.Null, streamInfo, cts.Token);
        }
        catch (HttpRequestException ex)
        {
            throw SkipException.ForSkip($"Skipping live YouTube test due to HTTP failure: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            throw SkipException.ForSkip($"Skipping live YouTube test because the request was cancelled: {ex.Message}");
        }
        catch (YoutubeExplodeException ex)
        {
            throw SkipException.ForSkip($"Skipping live YouTube test due to YouTube API error: {ex.Message}");
        }

        result.ShouldNotBeNull();
        result.Title.ShouldNotBeNull();
        
        // If metadata fetching failed (due to rate limiting, API changes, etc.), skip the test
        if (result.Title.StartsWith("YouTube Video ", StringComparison.OrdinalIgnoreCase))
        {
            throw SkipException.ForSkip($"Skipping live YouTube test because metadata could not be fetched (got fallback title: {result.Title})");
        }
        
        result.Title.ShouldContain("SOLID Principles");
        result.Markdown.ShouldContain("Managed Code");
        result.Markdown.ShouldContain("**Views:**");
        result.Segments.ShouldContain(segment => segment.Type == SegmentType.Metadata);
        result.Segments.ShouldContain(segment => segment.Type == SegmentType.Audio);

        var metadataSegment = result.Segments.First(segment => segment.Type == SegmentType.Metadata);
        metadataSegment.AdditionalMetadata.ShouldContainKey(MetadataKeys.Provider);
        metadataSegment.AdditionalMetadata[MetadataKeys.Provider].ShouldBe(MetadataValues.ProviderYouTube);
    }
}
