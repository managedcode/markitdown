using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using MarkItDown.Intelligence.Models;
using MarkItDown.Intelligence.Providers.Azure.VideoIndexer;
using Microsoft.Extensions.Logging;

namespace MarkItDown.Intelligence.Providers.Azure;

/// <summary>
/// Partial Azure Video Indexer integration. Uploads media and retrieves the primary transcript when credentials are provided.
/// </summary>
public sealed class AzureMediaTranscriptionProvider : IMediaTranscriptionProvider, IDisposable
{
    private readonly AzureMediaIntelligenceOptions _options;
    private readonly VideoIndexerClient _client;
    private bool _disposed;

    public AzureMediaTranscriptionProvider(AzureMediaIntelligenceOptions options, HttpClient? httpClient = null, ArmTokenService? armTokenService = null, ILogger? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.AccountId) || string.IsNullOrWhiteSpace(options.Location))
        {
            throw new ArgumentException("Azure Video Indexer account id and location must be provided.");
        }

        _client = new VideoIndexerClient(options, httpClient, armTokenService, logger);
    }

    public async Task<MediaTranscriptionResult?> TranscribeAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        var uploadResult = await _client.UploadAsync(stream, streamInfo, cancellationToken).ConfigureAwait(false);
        if (uploadResult is null)
        {
            return null;
        }

        await _client.WaitForProcessingAsync(uploadResult.Value.VideoId, uploadResult.Value.AccountAccessToken, cancellationToken).ConfigureAwait(false);

        using var index = await _client.GetVideoIndexAsync(uploadResult.Value.VideoId, uploadResult.Value.AccountAccessToken, cancellationToken).ConfigureAwait(false);
        if (index is null)
        {
            return null;
        }

        return ParseTranscript(index.RootElement, uploadResult.Value.VideoId);
    }

    private static TimeSpan? ParseTimeSpan(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var ts))
        {
            return ts;
        }

        return null;
    }

    private static MediaTranscriptionResult? ParseTranscript(JsonElement root, string videoId)
    {
        if (!root.TryGetProperty("videos", out var videos) || videos.GetArrayLength() == 0)
        {
            return null;
        }

        var insights = videos[0].GetProperty("insights");
        if (!insights.TryGetProperty("transcript", out var transcriptArray))
        {
            return null;
        }

        var segments = new List<MediaTranscriptSegment>();
        foreach (var item in transcriptArray.EnumerateArray())
        {
            var text = item.GetProperty("text").GetString() ?? string.Empty;
            var start = ParseTimeSpan(item.TryGetProperty("start", out var startNode) ? startNode.GetString() : null);
            var duration = ParseTimeSpan(item.TryGetProperty("duration", out var durationNode) ? durationNode.GetString() : null);
            var end = start.HasValue && duration.HasValue ? start + duration : null;
            var metadata = new Dictionary<string, string>
            {
                [MetadataKeys.VideoId] = videoId,
                [MetadataKeys.Provider] = MetadataValues.ProviderAzureVideoIndexer
            };

            segments.Add(new MediaTranscriptSegment(text, start, end, metadata));
        }

        if (segments.Count == 0)
        {
            return null;
        }

        var resultMetadata = new Dictionary<string, string>
        {
            [MetadataKeys.VideoId] = videoId,
            [MetadataKeys.Provider] = MetadataValues.ProviderAzureVideoIndexer
        };

        return new MediaTranscriptionResult(segments, metadata: resultMetadata);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
    }
}

/// <summary>
/// Retrieves ARM access tokens for Azure Video Indexer.
/// </summary>
public class ArmTokenService
{
    private readonly string? _armTokenOverride;
    private readonly DefaultAzureCredential _credential = new();
    private string? _cachedToken;
    private DateTimeOffset _expiry;
    private readonly object _lock = new();

    public ArmTokenService(string? armTokenOverride)
    {
        _armTokenOverride = armTokenOverride;
    }

    public virtual async Task<string?> GetArmTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_armTokenOverride))
        {
            return _armTokenOverride;
        }

        lock (_lock)
        {
            if (_cachedToken != null && DateTimeOffset.UtcNow < _expiry - TimeSpan.FromMinutes(5))
            {
                return _cachedToken;
            }
        }

        var token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { "https://management.azure.com/.default" }), cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            _cachedToken = token.Token;
            _expiry = token.ExpiresOn;
            return _cachedToken;
        }
    }
}
