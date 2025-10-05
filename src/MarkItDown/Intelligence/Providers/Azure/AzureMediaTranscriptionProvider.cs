using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using MarkItDown;
using MarkItDown.Intelligence.Models;

namespace MarkItDown.Intelligence.Providers.Azure;

/// <summary>
/// Partial Azure Video Indexer integration. Uploads media and retrieves the primary transcript when credentials are provided.
/// </summary>
public sealed class AzureMediaTranscriptionProvider : IMediaTranscriptionProvider, IDisposable
{
    private static readonly Uri VideoIndexerBaseUri = new("https://api.videoindexer.ai/");

    private readonly AzureMediaIntelligenceOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ArmTokenService _armTokenService;
    private bool _disposed;

    public AzureMediaTranscriptionProvider(AzureMediaIntelligenceOptions options, HttpClient? httpClient = null, ArmTokenService? armTokenService = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.AccountId) || string.IsNullOrWhiteSpace(options.Location))
        {
            throw new ArgumentException("Azure Video Indexer account id and location must be provided.");
        }

        _httpClient = httpClient ?? new HttpClient { BaseAddress = VideoIndexerBaseUri };
        _armTokenService = armTokenService ?? new ArmTokenService(options.ArmAccessToken);
    }

    public async Task<MediaTranscriptionResult?> TranscribeAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        // Acquire account access token
        var accountAccessToken = await GetAccountAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (accountAccessToken is null)
        {
            return null;
        }

        var fileName = !string.IsNullOrEmpty(streamInfo.FileName) ? streamInfo.FileName : $"upload_{Guid.NewGuid():N}";

        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(stream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(streamInfo.MimeType ?? "application/octet-stream");
        content.Add(streamContent, "file", fileName);

        var uploadUri = new Uri($"{_options.Location}/Accounts/{_options.AccountId}/Videos?name={Uri.EscapeDataString(fileName)}&accessToken={Uri.EscapeDataString(accountAccessToken)}", UriKind.Relative);
        using var uploadResponse = await _httpClient.PostAsync(uploadUri, content, cancellationToken).ConfigureAwait(false);
        uploadResponse.EnsureSuccessStatusCode();

        using var uploadStream = await uploadResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var uploadDoc = await JsonDocument.ParseAsync(uploadStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var videoId = uploadDoc.RootElement.GetProperty("id").GetString();
        if (string.IsNullOrEmpty(videoId))
        {
            return null;
        }

        // Wait for processing completion
        await WaitForProcessingAsync(videoId, accountAccessToken, cancellationToken).ConfigureAwait(false);

        var transcript = await DownloadTranscriptAsync(videoId, accountAccessToken, cancellationToken).ConfigureAwait(false);
        if (transcript is null)
        {
            return null;
        }

        return transcript;
    }

    private async Task<string?> GetAccountAccessTokenAsync(CancellationToken cancellationToken)
    {
        var armToken = await _armTokenService.GetArmTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(armToken))
        {
            return null;
        }

        var authUri = new Uri($"Auth/{_options.Location}/Accounts/{_options.AccountId}/AccessToken?allowEdit=true", UriKind.Relative);
        using var request = new HttpRequestMessage(HttpMethod.Get, authUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", armToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WaitForProcessingAsync(string videoId, string accessToken, CancellationToken cancellationToken)
    {
        var statusUri = new Uri($"{_options.Location}/Accounts/{_options.AccountId}/Videos/{videoId}/Index?accessToken={Uri.EscapeDataString(accessToken)}", UriKind.Relative);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var response = await _httpClient.GetAsync(statusUri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var state = json.RootElement.GetProperty("state").GetString();
            if (!string.Equals(state, "Processing", StringComparison.OrdinalIgnoreCase) && !string.Equals(state, "Uploaded", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<MediaTranscriptionResult?> DownloadTranscriptAsync(string videoId, string accessToken, CancellationToken cancellationToken)
    {
        var transcriptUri = new Uri($"{_options.Location}/Accounts/{_options.AccountId}/Videos/{videoId}/Index?accessToken={Uri.EscapeDataString(accessToken)}&language=English", UriKind.Relative);
        using var response = await _httpClient.GetAsync(transcriptUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(jsonStream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!json.RootElement.TryGetProperty("videos", out var videos) || videos.GetArrayLength() == 0)
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
            var start = ParseTimeSpan(item.GetProperty("start").GetString());
            var duration = ParseTimeSpan(item.GetProperty("duration").GetString());
            var end = start.HasValue && duration.HasValue ? start + duration : null;
            segments.Add(new MediaTranscriptSegment(text, start, end));
        }

        var metadata = new Dictionary<string, string>
        {
            [MetadataKeys.VideoId] = videoId,
            [MetadataKeys.Provider] = MetadataValues.ProviderAzureVideoIndexer
        };

        return new MediaTranscriptionResult(segments, metadata: metadata);
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
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
