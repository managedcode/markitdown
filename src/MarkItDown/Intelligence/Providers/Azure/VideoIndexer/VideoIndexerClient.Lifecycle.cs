using System.Net;
using System.Text.Json;

namespace MarkItDown.Intelligence.Providers.Azure.VideoIndexer;

internal sealed partial class VideoIndexerClient
{
    private const string SpeechOnlyIndexingPreset = "BasicAudio";
    private const string NoStreamingPreset = "NoStreaming";
    private const string TemporaryRetentionDays = "1";
    private const string DuplicateDetectionPageSize = "2";

    private string VideosPath => $"{location}/Accounts/{accountId}/Videos";

    private string VideoPath(string videoId) => $"{VideosPath}/{RequireRouteSegment(videoId, nameof(videoId))}";

    internal async Task<string> SubmitAsync(
        Stream stream, StreamInfo streamInfo, string externalId, string sourceLanguage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(streamInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLanguage);
        var id = RequireRouteSegment(externalId, nameof(externalId));
        var token = await RequireAccountAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        var query = BuildQueryString(new Dictionary<string, string>
        {
            ["name"] = id,
            ["externalId"] = id,
            ["language"] = sourceLanguage,
            ["privacy"] = "private",
            ["indexingPreset"] = SpeechOnlyIndexingPreset,
            ["streamingPreset"] = NoStreamingPreset,
            ["retentionPeriod"] = TemporaryRetentionDays
        });
        var requestUri = new Uri($"{VideosPath}?{query}", UriKind.Relative);
        var fileName = id + (streamInfo.Extension ?? Path.GetExtension(streamInfo.FileName));
        using var response = await UploadByStreamAsync(requestUri, token, stream, streamInfo, fileName, cancellationToken)
            .ConfigureAwait(false);
        using var document = await ReadResponseJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return ReadVideoId(document.RootElement);
    }

    internal async Task<string?> FindVideoIdAsync(string externalId, CancellationToken cancellationToken)
    {
        var id = RequireRouteSegment(externalId, nameof(externalId));
        var token = await RequireAccountAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var query = BuildQueryString(new Dictionary<string, string>
        {
            ["externalId"] = id,
            ["pageSize"] = DuplicateDetectionPageSize
        });
        var requestUri = new Uri($"{VideosPath}/Search?{query}", UriKind.Relative);
        using var document = await ReadVideoJsonAsync(requestUri, token, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            throw new FileConversionException("Azure Video Indexer search did not return a results array.");
        }

        string? videoId = null;
        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("externalId", out var external) || external.ValueKind != JsonValueKind.String ||
                !string.Equals(external.GetString(), id, StringComparison.Ordinal))
            {
                throw new FileConversionException("Azure Video Indexer search returned a different external ID.");
            }

            if (videoId is not null)
            {
                throw new FileConversionException("Azure Video Indexer returned duplicate recordings for one external ID.");
            }

            videoId = ReadVideoId(result);
        }

        return videoId;
    }

    internal async Task<JsonDocument> GetJobIndexAsync(string videoId, CancellationToken cancellationToken)
    {
        var requestUri = VideoIndexUri(videoId, language: null);
        var token = await RequireAccountAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadVideoJsonAsync(requestUri, token, cancellationToken).ConfigureAwait(false);
    }

    internal async Task DeleteAsync(string videoId, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(VideoPath(videoId), UriKind.Relative);
        var token = await RequireAccountAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        using var response = await SendVideoRequestAsync(HttpMethod.Delete, requestUri, token, null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    internal static AzureVideoIndexerTranscriptionState ReadJobState(JsonElement index) =>
        ReadProcessingState(index) switch
        {
            ProcessingStateUploaded or ProcessingStateProcessing => AzureVideoIndexerTranscriptionState.Pending,
            ProcessingStateProcessed => AzureVideoIndexerTranscriptionState.Processed,
            ProcessingStateFailed or ProcessingStateQuarantined => AzureVideoIndexerTranscriptionState.Failed,
            _ => throw new FileConversionException("Azure Video Indexer returned an unsupported processing state.")
        };

    private async Task<string> RequireAccountAccessTokenAsync(CancellationToken cancellationToken) =>
        await EnsureAccountAccessTokenAsync(cancellationToken).ConfigureAwait(false) ??
        throw new FileConversionException("Azure Video Indexer did not provide an account access token.");

    private static string ReadVideoId(JsonElement result)
    {
        if (!result.TryGetProperty("id", out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new FileConversionException("Azure Video Indexer did not return a recording ID.");
        }

        return RequireRouteSegment(value.GetString()!, "videoId");
    }
}
