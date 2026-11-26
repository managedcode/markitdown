using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Text.Json;
using MarkItDown;
using MarkItDown.Intelligence.Providers.Azure;
using Microsoft.Extensions.Logging;

namespace MarkItDown.Intelligence.Providers.Azure.VideoIndexer;

internal sealed partial class VideoIndexerClient : IDisposable
{
    private const string ProcessingStateUploaded = "Uploaded";
    private const string ProcessingStateProcessing = "Processing";
    private const string ProcessingStateProcessed = "Processed";
    private const string ProcessingStateFailed = "Failed";
    private const string ProcessingStateQuarantined = "Quarantined";

    private readonly AzureMediaIntelligenceOptions options;
    private readonly ArmTokenService armTokenService;
    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly TimeSpan pollingInterval;
    private readonly TimeSpan maxProcessingTime;
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

        if (options.PollingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("Azure Video Indexer polling interval must be greater than zero.", nameof(options));
        }

        if (options.MaxProcessingTime <= TimeSpan.Zero)
        {
            throw new ArgumentException("Azure Video Indexer max processing time must be greater than zero.", nameof(options));
        }

        accountId = RequireRouteSegment(options.AccountId, nameof(options.AccountId));
        location = RequireRouteSegment(options.Location, nameof(options.Location));
        resourceId = ResolveResourceId(options);
        pollingInterval = options.PollingInterval;
        maxProcessingTime = options.MaxProcessingTime;
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
            this.httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(VideoIndexerApiConstants.ApiEndpoint)
            };
            disposeHttpClient = true;
        }
    }

    public async Task<VideoIndexerUploadResult?> UploadAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(streamInfo);

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

        var uploadName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(uploadName))
        {
            uploadName = CreateUploadName("upload.bin");
        }

        var sourceVideoUrl = TryResolveVideoUrl(streamInfo.Url);
        if (!string.IsNullOrWhiteSpace(sourceVideoUrl))
        {
            logger?.LogDebug(
                "Using Azure Video Indexer URL upload for account {AccountId}.",
                accountId);
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            var queryValues = new Dictionary<string, string>
            {
                ["name"] = uploadName,
                ["privacy"] = "private"
            };

            if (!string.IsNullOrWhiteSpace(sourceVideoUrl))
            {
                queryValues["videoUrl"] = sourceVideoUrl;
            }

            var query = BuildQueryString(queryValues);

            var requestUri = new Uri($"{VideosPath}?{query}", UriKind.Relative);

            using var response = string.IsNullOrWhiteSpace(sourceVideoUrl)
                ? await UploadByStreamAsync(requestUri, accountToken, stream, streamInfo, fileName, cancellationToken).ConfigureAwait(false)
                : await SendVideoRequestAsync(HttpMethod.Post, requestUri, accountToken, null, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Conflict && attempt == 0)
            {
                uploadName = CreateUploadName(fileName);
                logger?.LogWarning(
                    "Azure Video Indexer upload name conflict for account {AccountId}. Retrying with a generated name.",
                    accountId);
                continue;
            }

            using var json = await ReadResponseJsonAsync(response, cancellationToken).ConfigureAwait(false);
            var videoId = json.RootElement.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(videoId))
            {
                return null;
            }

            return new VideoIndexerUploadResult(videoId, uploadName, accountToken);
        }

        throw new FileConversionException("Azure Video Indexer upload failed after retrying with a generated upload name.");
    }

    private async Task<HttpResponseMessage> SendVideoRequestAsync(
        HttpMethod method, Uri requestUri, string accountToken, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accountToken);
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> UploadByStreamAsync(
        Uri requestUri,
        string accountToken,
        Stream stream,
        StreamInfo streamInfo,
        string fileName,
        CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(new NonDisposingStream(stream));
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(streamInfo.MimeType ?? "application/octet-stream");
        content.Add(streamContent, "fileName", fileName);

        return await SendVideoRequestAsync(HttpMethod.Post, requestUri, accountToken, content, cancellationToken).ConfigureAwait(false);
    }

    public async Task WaitForProcessingAsync(string videoId, string accountToken, CancellationToken cancellationToken)
    {
        var statusUri = VideoIndexUri(videoId, null);
        var startedAt = Stopwatch.GetTimestamp();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var json = await ReadVideoJsonAsync(statusUri, accountToken, cancellationToken).ConfigureAwait(false);
            var state = ReadProcessingState(json.RootElement);
            var progress = TryReadProcessingProgress(json.RootElement);
            var elapsed = Stopwatch.GetElapsedTime(startedAt);

            if (string.Equals(state, ProcessingStateProcessed, StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogInformation(
                    "Video indexer processing completed for {VideoId} with state {State} in {ElapsedSeconds:F1}s (progress {Progress}).",
                    videoId,
                    state,
                    elapsed.TotalSeconds,
                    progress ?? "n/a");
                return;
            }

            if (string.Equals(state, ProcessingStateFailed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state, ProcessingStateQuarantined, StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogError(
                    "Video indexer processing failed for {VideoId}. State: {State}. Progress: {Progress}.",
                    videoId,
                    state,
                    progress ?? "n/a");
                throw new FileConversionException(
                    $"Azure Video Indexer processing failed for video '{videoId}' with state '{state}' and progress '{progress ?? "n/a"}'.");
            }

            if (!string.Equals(state, ProcessingStateProcessing, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(state, ProcessingStateUploaded, StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogError(
                    "Video indexer returned an unsupported state for {VideoId}. Progress: {Progress}.",
                    videoId,
                    progress ?? "n/a");
                throw new FileConversionException(
                    $"Azure Video Indexer returned an unsupported processing state for video '{videoId}'.");
            }

            if (elapsed > maxProcessingTime)
            {
                throw new FileConversionException(
                    $"Azure Video Indexer processing timed out for video '{videoId}' after {maxProcessingTime.TotalSeconds:F0}s. " +
                    $"Last state: '{state}', progress: '{progress ?? "n/a"}'.");
            }

            logger?.LogDebug(
                "Video indexer still processing {VideoId} (state {State}, progress {Progress}, elapsed {ElapsedSeconds:F1}s).",
                videoId,
                state,
                progress ?? "n/a",
                elapsed.TotalSeconds);
            await Task.Delay(pollingInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<JsonDocument> GetVideoIndexAsync(string videoId, string accountToken, string? language, CancellationToken cancellationToken) =>
        ReadVideoJsonAsync(VideoIndexUri(videoId, language), accountToken, cancellationToken);

    private Uri VideoIndexUri(string videoId, string? language)
    {
        var path = $"{VideoPath(videoId)}/Index";
        return string.IsNullOrWhiteSpace(language)
            ? new Uri(path, UriKind.Relative)
            : new Uri($"{path}?language={Uri.EscapeDataString(language)}", UriKind.Relative);
    }

    private async Task<JsonDocument> ReadVideoJsonAsync(Uri requestUri, string accountToken, CancellationToken cancellationToken)
    {
        using var response = await SendVideoRequestAsync(HttpMethod.Get, requestUri, accountToken, null, cancellationToken).ConfigureAwait(false);
        return await ReadResponseJsonAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadResponseJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
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

        if (LooksLikeVideoIndexerAccountAccessToken(armToken, out var accountTokenExpiry, out var permission))
        {
            EnsureUploadPermission(permission);
            logger?.LogDebug("Using provided token as Azure Video Indexer account access token for account {AccountId}.", accountId);
            cachedAccountToken = new AccountAccessToken(armToken, accountTokenExpiry);
            return armToken;
        }

        logger?.LogDebug("Refreshing Azure Video Indexer account access token for account {AccountId}.", accountId);
        var token = await AccountTokenProvider.GenerateAccountAccessTokenAsync(httpClient, armToken, resourceId, ArmAccessTokenPermission.Contributor, ArmAccessTokenScope.Account, cancellationToken).ConfigureAwait(false);
        cachedAccountToken = new AccountAccessToken(token.Token, token.ExpiresOn);
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

    private static string? TryResolveVideoUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.ToString();
    }

    private static string ResolveResourceId(AzureMediaIntelligenceOptions options)
    {
        string resource;
        if (!string.IsNullOrWhiteSpace(options.ResourceId))
        {
            resource = options.ResourceId;
        }
        else if (!string.IsNullOrWhiteSpace(options.SubscriptionId) &&
                 !string.IsNullOrWhiteSpace(options.ResourceGroup) &&
                 !string.IsNullOrWhiteSpace(options.AccountName))
        {
            resource = $"/subscriptions/{options.SubscriptionId}/resourceGroups/{options.ResourceGroup}/providers/Microsoft.VideoIndexer/accounts/{options.AccountName}";
        }
        else
        {
            throw new ArgumentException("Azure Video Indexer configuration must include either ResourceId or SubscriptionId/ResourceGroup/AccountName.");
        }

        return NormalizeResourceId(resource);
    }

    private static string NormalizeResourceId(string resourceId)
    {
        var normalized = ExtractResourcePath(resourceId);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Azure Video Indexer resource id must be provided.", nameof(resourceId));
        }

        var trimmed = normalized.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Azure Video Indexer resource id must be provided.", nameof(resourceId));
        }

        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (TryGetVideoIndexerAccountSegmentIndex(segments, out var accountSegmentIndex))
        {
            return "/" + string.Join("/", segments, 0, accountSegmentIndex + 2);
        }

        return "/" + trimmed;
    }

    private static string ExtractResourcePath(string resourceId)
    {
        var candidate = resourceId.Trim();
        var queryOrFragmentSeparator = candidate.IndexOfAny(new[] { '?', '#' });
        if (queryOrFragmentSeparator >= 0)
        {
            candidate = candidate[..queryOrFragmentSeparator];
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            candidate = uri.AbsolutePath;
        }

        return candidate;
    }

    private static bool TryGetVideoIndexerAccountSegmentIndex(string[] segments, out int accountSegmentIndex)
    {
        accountSegmentIndex = -1;

        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (!string.Equals(segments[index], "accounts", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index < 2)
            {
                continue;
            }

            if (!string.Equals(segments[index - 2], "providers", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(segments[index - 1], "Microsoft.VideoIndexer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            accountSegmentIndex = index;
            return true;
        }

        return false;
    }

    private static string ReadProcessingState(JsonElement root)
    {
        if (!root.TryGetProperty("state", out var stateElement))
        {
            throw new FileConversionException("Azure Video Indexer status response did not contain 'state'.");
        }

        var state = stateElement.GetString();
        if (string.IsNullOrWhiteSpace(state))
        {
            throw new FileConversionException("Azure Video Indexer status response contained an empty 'state'.");
        }

        return state;
    }

    private static string? TryReadProcessingProgress(JsonElement root)
    {
        if (root.TryGetProperty("processingProgress", out var rootProgress))
        {
            var value = ReadProgress(rootProgress);
            if (value is not null)
            {
                return value;
            }
        }

        if (root.TryGetProperty("videos", out var videos) &&
            videos.ValueKind == JsonValueKind.Array &&
            videos.GetArrayLength() > 0)
        {
            var firstVideo = videos[0];
            if (firstVideo.TryGetProperty("processingProgress", out var videoProgress))
            {
                var value = ReadProgress(videoProgress);
                if (value is not null)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? ReadProgress(JsonElement element)
    {
        var text = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };
        return int.TryParse(text?.TrimEnd('%'), NumberStyles.None, CultureInfo.InvariantCulture, out var percent) &&
               percent is >= 0 and <= 100
            ? percent.ToString(CultureInfo.InvariantCulture) + "%"
            : null;
    }

    private static string RequireRouteSegment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            throw new ArgumentException("Expected a single Azure Video Indexer route identifier.", parameterName);
        }

        return value;
    }

    private static string CreateUploadName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(nameWithoutExtension))
        {
            nameWithoutExtension = "upload";
        }

        return $"{nameWithoutExtension}-{Guid.NewGuid():N}{extension}";
    }

    private static bool LooksLikeVideoIndexerAccountAccessToken(string token, out DateTimeOffset? expiresOn, out string? permission)
    {
        expiresOn = null;
        permission = null;

        if (!TryParseJwtPayload(token, out var payload))
        {
            return false;
        }

        if (!payload.TryGetProperty("aud", out var audNode))
        {
            return false;
        }

        var audience = audNode.GetString();
        if (!string.Equals(audience, "https://api.videoindexer.ai/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (payload.TryGetProperty("exp", out var expNode) &&
            expNode.ValueKind == JsonValueKind.Number &&
            expNode.TryGetInt64(out var expUnix))
        {
            expiresOn = DateTimeOffset.FromUnixTimeSeconds(expUnix);
        }

        if (payload.TryGetProperty("Permission", out var permissionNode) &&
            permissionNode.ValueKind == JsonValueKind.String)
        {
            permission = permissionNode.GetString();
        }

        return true;
    }

    private static void EnsureUploadPermission(string? permission)
    {
        if (!string.Equals(permission, "Reader", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new FileConversionException(
            "Configured Azure Video Indexer token is read-only (Permission=Reader). " +
            "Uploading media requires Contributor permission. " +
            "Provide ArmAccessToken as an ARM token with contributor access or an account token with Contributor permission.");
    }

    private static bool TryParseJwtPayload(string token, out JsonElement payload)
    {
        payload = default;

        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return false;
        }

        var payloadPart = parts[1];
        var padLength = 4 - (payloadPart.Length % 4);
        if (padLength is > 0 and < 4)
        {
            payloadPart = payloadPart.PadRight(payloadPart.Length + padLength, '=');
        }

        payloadPart = payloadPart.Replace('-', '+').Replace('_', '/');

        try
        {
            var bytes = Convert.FromBase64String(payloadPart);
            using var json = JsonDocument.Parse(bytes);
            payload = json.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

internal readonly record struct VideoIndexerUploadResult(string VideoId, string FileName, string AccountAccessToken);

internal sealed class NonDisposingStream(Stream inner) : Stream
{
    private readonly Stream inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override void Flush() => inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void SetLength(long value) => inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => inner.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => inner.WriteAsync(buffer, cancellationToken);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => inner.WriteAsync(buffer, offset, count, cancellationToken);

    protected override void Dispose(bool disposing)
    {
    }

    public override async ValueTask DisposeAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
