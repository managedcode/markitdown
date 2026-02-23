using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown.YouTube;
using Shouldly;
using Xunit;
using YoutubeExplode;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace MarkItDown.Tests.Converters;

public sealed class YouTubeLiveFactAttribute : FactAttribute
{
    public YouTubeLiveFactAttribute()
    {
        Skip = YouTubeUrlConverterLiveTests.LiveProbe.Value.SkipReason;
    }
}

public sealed class YouTubeDownloadLiveFactAttribute : FactAttribute
{
    public YouTubeDownloadLiveFactAttribute()
    {
        Skip = YouTubeUrlConverterLiveTests.LiveDownloadProbe.Value.SkipReason;
    }
}

/// <summary>
/// Live integration tests that exercise the real YouTube metadata provider against the public API.
/// The probe skips automatically when network/API access is unavailable.
/// </summary>
public sealed class YouTubeUrlConverterLiveTests
{
    private const string SolidPrinciplesVideoId = "8hnpIIamb6k";
    private const string DownloadVerificationVideoId = "uMMqwLNep4s";
    private static readonly Uri DownloadVerificationVideoUrl = new($"https://youtu.be/{DownloadVerificationVideoId}");

    internal static readonly Lazy<LiveProbeResult> LiveProbe = new(ProbeLiveVideo, LazyThreadSafetyMode.ExecutionAndPublication);
    internal static readonly Lazy<LiveAvailabilityProbeResult> LiveDownloadProbe = new(ProbeLiveDownload, LazyThreadSafetyMode.ExecutionAndPublication);

    [YouTubeLiveFact]
    public void YoutubeExplodeMetadataProvider_WithLiveVideo_ReturnsMetadata()
    {
        var metadata = LiveProbe.Value.Metadata ?? throw new InvalidOperationException("Live probe expected metadata when no skip reason is set.");

        metadata.Title.ShouldContain("SOLID Principles");
        metadata.ChannelTitle.ShouldContain("Managed Code");
        metadata.WatchUrl.ToString().ShouldContain(SolidPrinciplesVideoId);
        metadata.AdditionalMetadata.ShouldContainKey(MetadataKeys.Provider);
        metadata.AdditionalMetadata[MetadataKeys.Provider].ShouldBe(MetadataValues.ProviderYouTube);
    }

    [YouTubeDownloadLiveFact]
    public async Task YoutubeExplodeVideoDownloader_WithLiveVideo_DownloadsPhysicalFile()
    {
        var downloader = new YoutubeExplodeVideoDownloader();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        await using var media = await downloader.DownloadAsync(DownloadVerificationVideoId, DownloadVerificationVideoUrl, cts.Token);

        File.Exists(media.FilePath).ShouldBeTrue();
        var fileInfo = new FileInfo(media.FilePath);
        fileInfo.Exists.ShouldBeTrue();
        fileInfo.Length.ShouldBeGreaterThan(0);

        media.StreamInfo.LocalPath.ShouldBe(media.FilePath);
        media.StreamInfo.Url.ShouldNotBeNullOrWhiteSpace();
        media.StreamInfo.MimeType.ShouldStartWith("video/");
        media.StreamInfo.Extension.ShouldNotBeNullOrWhiteSpace();
    }

    private static LiveProbeResult ProbeLiveVideo()
    {
        var provider = new YoutubeExplodeMetadataProvider();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        try
        {
            var metadata = provider.GetVideoAsync(SolidPrinciplesVideoId, cts.Token).GetAwaiter().GetResult();
            if (metadata is null)
            {
                return LiveProbeResult.Skip("Live YouTube metadata unavailable (provider returned null metadata).");
            }

            if (string.IsNullOrWhiteSpace(metadata.Title))
            {
                return LiveProbeResult.Skip("Live YouTube metadata unavailable (title was empty).");
            }

            return LiveProbeResult.Success(metadata);
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

    private static LiveAvailabilityProbeResult ProbeLiveDownload()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var client = new YoutubeClient();

        try
        {
            var videoId = VideoId.Parse(DownloadVerificationVideoId);
            var manifest = client.Videos.Streams.GetManifestAsync(videoId, cts.Token).GetAwaiter().GetResult();
            var muxedStream = manifest.GetMuxedStreams().TryGetWithHighestVideoQuality()
                ?? manifest.GetMuxedStreams().TryGetWithHighestBitrate();

            if (muxedStream is null)
            {
                return LiveAvailabilityProbeResult.Skip("Live YouTube download unavailable (no muxed video streams were resolved).");
            }

            return LiveAvailabilityProbeResult.Available();
        }
        catch (HttpRequestException ex)
        {
            return LiveAvailabilityProbeResult.Skip($"Live YouTube download unavailable due to HTTP failure: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            return LiveAvailabilityProbeResult.Skip($"Live YouTube download unavailable because request was cancelled: {ex.Message}");
        }
        catch (YoutubeExplodeException ex)
        {
            return LiveAvailabilityProbeResult.Skip($"Live YouTube download unavailable due to YouTube API error: {ex.Message}");
        }
    }

    internal sealed record LiveProbeResult(YouTubeMetadata? Metadata, string? SkipReason)
    {
        public static LiveProbeResult Success(YouTubeMetadata metadata)
        {
            return new(metadata, null);
        }

        public static LiveProbeResult Skip(string reason)
        {
            return new(null, reason);
        }
    }

    internal sealed record LiveAvailabilityProbeResult(string? SkipReason)
    {
        public static LiveAvailabilityProbeResult Available()
        {
            return new((string?)null);
        }

        public static LiveAvailabilityProbeResult Skip(string reason)
        {
            return new(reason);
        }
    }
}
