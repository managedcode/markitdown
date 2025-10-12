using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MarkItDown.Intelligence.Providers.Azure;
using Microsoft.Extensions.Logging;

namespace MarkItDown.Intelligence.Providers.Azure.VideoIndexer;

internal sealed class VideoIndexerClient : IDisposable
{
    private readonly AzureMediaIntelligenceOptions options;
    private readonly ArmTokenService armTokenService;
    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly TimeSpan pollingInterval = TimeSpan.FromSeconds(5);
    private readonly string accountId;
    private readonly string location;
    private readonly string resourceId;
    private readonly ILogger? logger;

    private AccountAccessToken? cachedAccountToken;

    public VideoIndexerClient(AzureMediaIntelligenceOptions options, HttpClient? httpClient = null, ArmTokenService? armTokenService = null, ILogger? logger = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.AccountId))
        {
            throw new ArgumentException("Azure Video Indexer account id must be provided.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Location))
        {
            throw new ArgumentException("Azure Video Indexer location must be provided.", nameof(options));
        }

        accountId = options.AccountId;
        location = options.Location;
        resourceId = ResolveResourceId(options);
        armTokenService = armTokenService ?? new ArmTokenService(options.ArmAccessToken);
        this.armTokenService = armTokenService;
        this.logger = logger;

        if (httpClient is not null)
        {
            this.httpClient = httpClient;
            disposeHttpClient = false;
        }
        else
        {
            this.httpClient = new HttpClient
            {
                BaseAddress = new Uri(VideoIndexerApiConstants.ApiEndpoint)
            };
            disposeHttpClient = true;
        }
    }

    public async Task<VideoIndexerUploadResult?> UploadAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        var accountToken = await EnsureAccountAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (accountToken is null)
        {
            return null;
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        var fileName = streamInfo.FileName;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            var extension = string.IsNullOrWhiteSpace(streamInfo.Extension) ? ".bin" : streamInfo.Extension;
            fileName = $"upload_{Guid.NewGuid():N}{extension}";
        }

        var query = BuildQueryString(new Dictionary<string, string>
        {
            ["name"] = fileName,
            ["accessToken"] = accountToken,
            ["privacy"] = "private"
        });

        var requestUri = new Uri($"{location}/Accounts/{accountId}/Videos?{query}", UriKind.Relative);

        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(stream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(streamInfo.MimeType ?? "application/octet-stream");
        content.Add(streamContent, "file", fileName);

        using var response = await httpClient.PostAsync(requestUri, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var videoId = json.RootElement.GetProperty("id").GetString();
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return null;
        }

        return new VideoIndexerUploadResult(videoId, fileName, accountToken);
    }

    public async Task WaitForProcessingAsync(string videoId, string accountToken, CancellationToken cancellationToken)
    {
        var statusUri = new Uri($"{location}/Accounts/{accountId}/Videos/{videoId}/Index?{BuildQueryString(new Dictionary<string, string>
        {
            ["accessToken"] = accountToken
        })}", UriKind.Relative);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var response = await httpClient.GetAsync(statusUri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var state = json.RootElement.GetProperty("state").GetString();
            if (!string.Equals(state, "Processing", StringComparison.OrdinalIgnoreCase) && !string.Equals(state, "Uploaded", StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogInformation("Video indexer processing state for {VideoId}: {State}", videoId, state);
                return;
            }

            logger?.LogDebug("Video indexer still processing {VideoId} (state {State})", videoId, state);
            await Task.Delay(pollingInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<JsonDocument?> GetVideoIndexAsync(string videoId, string accountToken, CancellationToken cancellationToken)
    {
        var requestUri = new Uri($"{location}/Accounts/{accountId}/Videos/{videoId}/Index?{BuildQueryString(new Dictionary<string, string>
        {
            ["accessToken"] = accountToken,
            ["language"] = "English"
        })}", UriKind.Relative);

        using var response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (disposeHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private async Task<string?> EnsureAccountAccessTokenAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (cachedAccountToken.HasValue && !cachedAccountToken.Value.IsExpired(now))
        {
            return cachedAccountToken.Value.Token;
        }

        var armToken = await armTokenService.GetArmTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(armToken))
        {
            return null;
        }

        var token = await AccountTokenProvider.GenerateAccountAccessTokenAsync(httpClient, armToken, resourceId, ArmAccessTokenPermission.Contributor, ArmAccessTokenScope.Account, cancellationToken).ConfigureAwait(false);
        cachedAccountToken = token with { Token = token.Token }; // struct copy
        return token.Token;
    }

    private static string BuildQueryString(IReadOnlyDictionary<string, string> values)
    {
        var builder = new StringBuilder();
        var first = true;
        foreach (var kvp in values)
        {
            if (string.IsNullOrWhiteSpace(kvp.Value))
            {
                continue;
            }

            if (!first)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(kvp.Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(kvp.Value));
            first = false;
        }

        return builder.ToString();
    }

    private static string ResolveResourceId(AzureMediaIntelligenceOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ResourceId))
        {
            return options.ResourceId;
        }

        if (!string.IsNullOrWhiteSpace(options.SubscriptionId) &&
            !string.IsNullOrWhiteSpace(options.ResourceGroup) &&
            !string.IsNullOrWhiteSpace(options.AccountName))
        {
            return $"/subscriptions/{options.SubscriptionId}/resourceGroups/{options.ResourceGroup}/providers/Microsoft.VideoIndexer/accounts/{options.AccountName}";
        }

        throw new ArgumentException("Azure Video Indexer configuration must include either ResourceId or SubscriptionId/ResourceGroup/AccountName.");
    }
}

internal readonly record struct VideoIndexerUploadResult(string VideoId, string FileName, string AccountAccessToken);
