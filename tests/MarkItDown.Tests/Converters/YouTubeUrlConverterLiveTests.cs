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
using YoutubeExplode.Exceptions;

namespace MarkItDown.Tests.Converters;

public sealed class YouTubeLiveFactAttribute : FactAttribute
{
    public YouTubeLiveFactAttribute()
    {
        Skip = YouTubeUrlConverterLiveTests.LiveProbe.Value.SkipReason;
    }
}

/// <summary>
/// Live integration tests that exercise the YouTube converter against the public API.
/// These tests rely on network connectivity and will be skipped automatically when YouTube
/// metadata cannot be retrieved (for example, when the network is unavailable or rate limited).
/// </summary>
public sealed class YouTubeUrlConverterLiveTests
{
    private const string SolidPrinciplesVideoUrl = "https://www.youtube.com/watch?v=8hnpIIamb6k";
    private const string FallbackTitlePrefix = "YouTube Video ";

    internal static readonly Lazy<LiveProbeResult> LiveProbe = new(ProbeLiveVideo, LazyThreadSafetyMode.ExecutionAndPublication);

    [YouTubeLiveFact]
    public void ConvertAsync_WithLiveVideo_FetchesMetadataFromYouTube()
    {
        var result = LiveProbe.Value.Result ?? throw new InvalidOperationException("Live probe expected conversion result when no skip reason is set.");
        var title = result.Title ?? throw new InvalidOperationException("Live probe returned null title.");
        title.StartsWith(FallbackTitlePrefix, StringComparison.OrdinalIgnoreCase).ShouldBeFalse();

        title.ShouldContain("SOLID Principles");
        result.Markdown.ShouldContain("Managed Code");
        result.Markdown.ShouldContain("**Views:**");
        result.Segments.ShouldContain(segment => segment.Type == SegmentType.Metadata);
        result.Segments.ShouldContain(segment => segment.Type == SegmentType.Audio);

        var metadataSegment = result.Segments.First(segment => segment.Type == SegmentType.Metadata);
        metadataSegment.AdditionalMetadata.ShouldContainKey(MetadataKeys.Provider);
        metadataSegment.AdditionalMetadata[MetadataKeys.Provider].ShouldBe(MetadataValues.ProviderYouTube);
    }

    private static LiveProbeResult ProbeLiveVideo()
    {
        var converter = new YouTubeUrlConverter();
        var streamInfo = new StreamInfo(url: SolidPrinciplesVideoUrl);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        try
        {
            var result = converter.ConvertAsync(Stream.Null, streamInfo, cts.Token).GetAwaiter().GetResult();
            var title = result.Title;
            if (title is null)
            {
                return LiveProbeResult.Skip("Live YouTube metadata unavailable (title was null).");
            }

            if (title.StartsWith(FallbackTitlePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return LiveProbeResult.Skip($"Live YouTube metadata unavailable (fallback title returned: {title}).");
            }

            return LiveProbeResult.Success(result);
        }
        catch (HttpRequestException ex)
        {
            return LiveProbeResult.Skip($"Live YouTube metadata unavailable due to HTTP failure: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            return LiveProbeResult.Skip($"Live YouTube metadata unavailable because request was cancelled: {ex.Message}");
        }
        catch (YoutubeExplodeException ex)
        {
            return LiveProbeResult.Skip($"Live YouTube metadata unavailable due to YouTube API error: {ex.Message}");
        }
    }

    internal sealed record LiveProbeResult(DocumentConverterResult? Result, string? SkipReason)
    {
        public static LiveProbeResult Success(DocumentConverterResult result)
        {
            return new(result, null);
        }

        public static LiveProbeResult Skip(string reason)
        {
            return new(null, reason);
        }
    }
}
