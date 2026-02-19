using System.Globalization;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Azure.Core;
using Azure.Identity;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using MarkItDown.Intelligence.Providers.Azure.VideoIndexer;
using Microsoft.Extensions.Logging;

namespace MarkItDown.Intelligence.Providers.Azure;

/// <summary>
/// Partial Azure Video Indexer integration. Uploads media and retrieves the primary transcript when credentials are provided.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class AzureMediaTranscriptionProvider : IMediaTranscriptionProvider, IDisposable
{
    private readonly AzureMediaIntelligenceOptions _options;
    private readonly VideoIndexerClient _client;
    private readonly ILogger? _logger;
    private bool _disposed;

    public AzureMediaTranscriptionProvider(AzureMediaIntelligenceOptions options, HttpClient? httpClient = null, ArmTokenService? armTokenService = null, ILogger? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.AccountId) || string.IsNullOrWhiteSpace(options.Location))
        {
            throw new ArgumentException("Azure Video Indexer account id and location must be provided.");
        }

        _client = new VideoIndexerClient(options, httpClient, armTokenService, logger);
        _logger = logger;
    }

    public async Task<MediaTranscriptionResult?> TranscribeAsync(Stream stream, StreamInfo streamInfo, MediaTranscriptionRequest? request = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var activity = MarkItDownDiagnostics.DefaultActivitySource.StartActivity(MarkItDownDiagnostics.ActivityNameMediaTranscription, ActivityKind.Internal);
        activity?.SetTag("markitdown.media.provider", MetadataValues.ProviderAzureVideoIndexer);
        if (!string.IsNullOrWhiteSpace(_options.AccountId))
        {
            activity?.SetTag("markitdown.azure.videoIndexer.accountId", _options.AccountId);
        }

        if (!string.IsNullOrWhiteSpace(_options.Location))
        {
            activity?.SetTag("markitdown.azure.videoIndexer.location", _options.Location);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var uploadResult = await _client.UploadAsync(stream, streamInfo, cancellationToken).ConfigureAwait(false);
            if (uploadResult is null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Azure Video Indexer upload returned no video id.");
                return null;
            }

            activity?.SetTag("markitdown.azure.videoIndexer.videoId", uploadResult.Value.VideoId);

            await _client.WaitForProcessingAsync(uploadResult.Value.VideoId, uploadResult.Value.AccountAccessToken, cancellationToken).ConfigureAwait(false);

            using var index = await _client.GetVideoIndexAsync(uploadResult.Value.VideoId, uploadResult.Value.AccountAccessToken, request?.Language, cancellationToken).ConfigureAwait(false);
            if (index is null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Azure Video Indexer returned null index document.");
                return null;
            }

            var result = ParseTranscript(index.RootElement, uploadResult.Value.VideoId, request);

            stopwatch.Stop();
            activity?.SetTag("markitdown.media.transcription.durationMs", stopwatch.Elapsed.TotalMilliseconds);
            activity?.SetTag("markitdown.media.transcription.segmentCount", result?.Segments.Count ?? 0);
            if (!string.IsNullOrWhiteSpace(result?.Language))
            {
                activity?.SetTag("markitdown.media.transcription.language", result.Language);
            }

            activity?.SetStatus(result is null ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetTag("markitdown.media.transcription.durationMs", stopwatch.Elapsed.TotalMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger?.LogWarning(ex, "Azure Video Indexer transcription failed for source {Source}.", streamInfo.FileName ?? streamInfo.Url ?? "stream");
            throw;
        }
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

    private static string? ReadScalarValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static MediaTranscriptionResult? ParseTranscript(JsonElement root, string videoId, MediaTranscriptionRequest? request)
    {
        if (!root.TryGetProperty("videos", out var videos) || videos.GetArrayLength() == 0)
        {
            return null;
        }

        var video = videos[0];
        if (!video.TryGetProperty("insights", out var insights))
        {
            return null;
        }

        var state = root.TryGetProperty("state", out var stateNode)
            ? ReadScalarValue(stateNode)
            : null;
        var indexId = video.TryGetProperty("id", out var videoIdNode)
            ? ReadScalarValue(videoIdNode)
            : insights.TryGetProperty("id", out var insightsIdNode)
                ? ReadScalarValue(insightsIdNode)
                : null;
        var progress = video.TryGetProperty("processingProgress", out var videoProgressNode)
            ? ReadScalarValue(videoProgressNode)
            : root.TryGetProperty("processingProgress", out var rootProgressNode)
                ? ReadScalarValue(rootProgressNode)
                : null;

        if (!insights.TryGetProperty("transcript", out var transcriptArray))
        {
            return null;
        }

        var speakers = ParseSpeakers(insights);
        var sentiments = ParseSentiments(insights);
        var topicInsights = ParseNamedInsights(insights, "topics", "name");
        var keywordInsights = ParseNamedInsights(insights, "keywords", "text");

        var detectedLanguage = insights.TryGetProperty("language", out var languageNode)
            ? languageNode.GetString()
            : null;
        var effectiveLanguage = !string.IsNullOrWhiteSpace(request?.Language)
            ? request!.Language
            : detectedLanguage;

        var segments = new List<MediaTranscriptSegment>();
        foreach (var item in transcriptArray.EnumerateArray())
        {
            var text = item.TryGetProperty("text", out var textNode)
                ? textNode.GetString() ?? string.Empty
                : string.Empty;

            var (start, end) = ParseSegmentTimeRange(item);
            var metadata = new Dictionary<string, string>
            {
                [MetadataKeys.VideoId] = videoId,
                [MetadataKeys.Provider] = MetadataValues.ProviderAzureVideoIndexer
            };

            if (!string.IsNullOrWhiteSpace(state))
            {
                metadata[MetadataKeys.VideoIndexerState] = state!;
            }

            if (!string.IsNullOrWhiteSpace(indexId))
            {
                metadata[MetadataKeys.VideoIndexerIndexId] = indexId!;
            }

            if (!string.IsNullOrWhiteSpace(progress))
            {
                metadata[MetadataKeys.VideoIndexerProgress] = progress!;
            }

            if (!string.IsNullOrWhiteSpace(effectiveLanguage))
            {
                metadata[MetadataKeys.Language] = effectiveLanguage!;
            }

            if (item.TryGetProperty("confidence", out var confidenceNode) &&
                confidenceNode.ValueKind == JsonValueKind.Number &&
                confidenceNode.TryGetDouble(out var confidence))
            {
                metadata[MetadataKeys.Confidence] = confidence.ToString("0.####", CultureInfo.InvariantCulture);
            }

            if (item.TryGetProperty("speakerId", out var speakerIdNode) &&
                speakerIdNode.ValueKind == JsonValueKind.Number &&
                speakerIdNode.TryGetInt32(out var speakerId))
            {
                metadata[MetadataKeys.SpeakerId] = speakerId.ToString(CultureInfo.InvariantCulture);
                if (speakers.TryGetValue(speakerId, out var speakerName) && !string.IsNullOrWhiteSpace(speakerName))
                {
                    metadata[MetadataKeys.Speaker] = speakerName;
                }
            }

            if (TryGetMatchingSentiment(start, end, sentiments, out var matchedSentiment))
            {
                metadata[MetadataKeys.Sentiment] = matchedSentiment.Type;
                if (matchedSentiment.Score.HasValue)
                {
                    metadata[MetadataKeys.SentimentScore] = matchedSentiment.Score.Value.ToString("0.###", CultureInfo.InvariantCulture);
                }
            }

            var overlappingTopics = GetOverlappingInsightNames(start, end, topicInsights, 4);
            if (overlappingTopics.Count > 0)
            {
                metadata[MetadataKeys.Topics] = string.Join(", ", overlappingTopics);
            }

            var overlappingKeywords = GetOverlappingInsightNames(start, end, keywordInsights, 6);
            if (overlappingKeywords.Count > 0)
            {
                metadata[MetadataKeys.Keywords] = string.Join(", ", overlappingKeywords);
            }

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

        if (!string.IsNullOrWhiteSpace(state))
        {
            resultMetadata[MetadataKeys.VideoIndexerState] = state!;
        }

        if (!string.IsNullOrWhiteSpace(indexId))
        {
            resultMetadata[MetadataKeys.VideoIndexerIndexId] = indexId!;
        }

        if (!string.IsNullOrWhiteSpace(progress))
        {
            resultMetadata[MetadataKeys.VideoIndexerProgress] = progress!;
        }

        if (!string.IsNullOrWhiteSpace(effectiveLanguage))
        {
            resultMetadata[MetadataKeys.Language] = effectiveLanguage!;
        }

        if (insights.TryGetProperty("duration", out var durationNode) && !string.IsNullOrWhiteSpace(durationNode.GetString()))
        {
            resultMetadata[MetadataKeys.Duration] = durationNode.GetString()!;
        }

        if (speakers.Count > 0)
        {
            resultMetadata[MetadataKeys.SpeakerCount] = speakers.Count.ToString(CultureInfo.InvariantCulture);
            resultMetadata[MetadataKeys.Speakers] = string.Join(", ", speakers.OrderBy(kvp => kvp.Key).Select(static kvp => kvp.Value));
        }

        var sentimentSummary = BuildSentimentSummary(sentiments);
        if (!string.IsNullOrWhiteSpace(sentimentSummary))
        {
            resultMetadata[MetadataKeys.Sentiments] = sentimentSummary;
        }

        var topicsSummary = BuildInsightSummary(insights, "topics", "name", 8);
        if (!string.IsNullOrWhiteSpace(topicsSummary))
        {
            resultMetadata[MetadataKeys.Topics] = topicsSummary;
        }

        var keywordsSummary = BuildInsightSummary(insights, "keywords", "text", 10);
        if (!string.IsNullOrWhiteSpace(keywordsSummary))
        {
            resultMetadata[MetadataKeys.Keywords] = keywordsSummary;
        }

        var labelsSummary = BuildInsightSummary(insights, "labels", "name", 10);
        if (!string.IsNullOrWhiteSpace(labelsSummary))
        {
            resultMetadata[MetadataKeys.Labels] = labelsSummary;
        }

        var locationSummary = BuildInsightSummary(insights, "namedLocations", "name", 6);
        if (!string.IsNullOrWhiteSpace(locationSummary))
        {
            resultMetadata[MetadataKeys.NamedLocations] = locationSummary;
        }

        if (TryReadSpeakerStatistics(insights, out var wordCount, out var fragmentCount, out var longestMonologSeconds))
        {
            if (wordCount > 0)
            {
                resultMetadata[MetadataKeys.WordCount] = wordCount.ToString(CultureInfo.InvariantCulture);
            }

            if (fragmentCount > 0)
            {
                resultMetadata[MetadataKeys.FragmentCount] = fragmentCount.ToString(CultureInfo.InvariantCulture);
            }

            if (longestMonologSeconds > 0)
            {
                resultMetadata[MetadataKeys.LongestMonologSeconds] = longestMonologSeconds.ToString(CultureInfo.InvariantCulture);
            }
        }

        return new MediaTranscriptionResult(segments, effectiveLanguage, resultMetadata);
    }

    private static Dictionary<int, string> ParseSpeakers(JsonElement insights)
    {
        var result = new Dictionary<int, string>();
        if (!insights.TryGetProperty("speakers", out var speakersNode) || speakersNode.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var speaker in speakersNode.EnumerateArray())
        {
            if (!speaker.TryGetProperty("id", out var idNode) ||
                idNode.ValueKind != JsonValueKind.Number ||
                !idNode.TryGetInt32(out var id))
            {
                continue;
            }

            var name = speaker.TryGetProperty("name", out var nameNode)
                ? nameNode.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"Speaker #{id}";
            }

            result[id] = name!;
        }

        return result;
    }

    private static List<SentimentInsight> ParseSentiments(JsonElement insights)
    {
        var result = new List<SentimentInsight>();
        if (!insights.TryGetProperty("sentiments", out var sentimentsNode) || sentimentsNode.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var sentiment in sentimentsNode.EnumerateArray())
        {
            var type = sentiment.TryGetProperty("sentimentType", out var typeNode)
                ? typeNode.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            double? score = null;
            if (sentiment.TryGetProperty("averageScore", out var scoreNode) &&
                scoreNode.ValueKind == JsonValueKind.Number &&
                scoreNode.TryGetDouble(out var parsedScore))
            {
                score = parsedScore;
            }

            var ranges = ParseRanges(sentiment);
            if (ranges.Count == 0)
            {
                result.Add(new SentimentInsight(type!, score, null, null));
                continue;
            }

            foreach (var range in ranges)
            {
                result.Add(new SentimentInsight(type!, score, range.Start, range.End));
            }
        }

        return result;
    }

    private static List<TimedNamedInsight> ParseNamedInsights(JsonElement insights, string propertyName, string textPropertyName)
    {
        var result = new List<TimedNamedInsight>();
        if (!insights.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in node.EnumerateArray())
        {
            var name = item.TryGetProperty(textPropertyName, out var nameNode)
                ? nameNode.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            double? confidence = null;
            if (item.TryGetProperty("confidence", out var confidenceNode) &&
                confidenceNode.ValueKind == JsonValueKind.Number &&
                confidenceNode.TryGetDouble(out var parsedConfidence))
            {
                confidence = parsedConfidence;
            }

            var ranges = ParseRanges(item);
            if (ranges.Count == 0)
            {
                result.Add(new TimedNamedInsight(name!, null, null, confidence));
                continue;
            }

            foreach (var range in ranges)
            {
                result.Add(new TimedNamedInsight(name!, range.Start, range.End, confidence));
            }
        }

        return result;
    }

    private static (TimeSpan? Start, TimeSpan? End) ParseSegmentTimeRange(JsonElement item)
    {
        var ranges = ParseRanges(item);
        if (ranges.Count > 0)
        {
            var start = ranges.Min(static r => r.Start ?? TimeSpan.MaxValue);
            var end = ranges.Max(static r => r.End ?? TimeSpan.Zero);
            return (start == TimeSpan.MaxValue ? null : start, end == TimeSpan.Zero ? null : end);
        }

        var startFallback = ParseTimeSpan(item.TryGetProperty("start", out var startNode) ? startNode.GetString() : null);
        var durationFallback = ParseTimeSpan(item.TryGetProperty("duration", out var durationNode) ? durationNode.GetString() : null);
        var endFallback = item.TryGetProperty("end", out var endNode) ? ParseTimeSpan(endNode.GetString()) : null;
        if (!endFallback.HasValue && startFallback.HasValue && durationFallback.HasValue)
        {
            endFallback = startFallback + durationFallback;
        }

        return (startFallback, endFallback);
    }

    private static List<(TimeSpan? Start, TimeSpan? End)> ParseRanges(JsonElement item)
    {
        var ranges = new List<(TimeSpan? Start, TimeSpan? End)>();
        if (!item.TryGetProperty("instances", out var instancesNode) || instancesNode.ValueKind != JsonValueKind.Array)
        {
            return ranges;
        }

        foreach (var instance in instancesNode.EnumerateArray())
        {
            var start = ParseTimeSpan(instance.TryGetProperty("start", out var startNode) ? startNode.GetString() : null)
                ?? ParseTimeSpan(instance.TryGetProperty("adjustedStart", out var adjustedStartNode) ? adjustedStartNode.GetString() : null);

            var end = ParseTimeSpan(instance.TryGetProperty("end", out var endNode) ? endNode.GetString() : null)
                ?? ParseTimeSpan(instance.TryGetProperty("adjustedEnd", out var adjustedEndNode) ? adjustedEndNode.GetString() : null);

            if (start.HasValue || end.HasValue)
            {
                ranges.Add((start, end));
            }
        }

        return ranges;
    }

    private static bool TryGetMatchingSentiment(TimeSpan? segmentStart, TimeSpan? segmentEnd, IReadOnlyList<SentimentInsight> sentiments, out SentimentInsight matched)
    {
        matched = default;
        if (sentiments.Count == 0)
        {
            return false;
        }

        var maxOverlap = double.MinValue;
        foreach (var sentiment in sentiments)
        {
            var overlap = CalculateOverlapSeconds(segmentStart, segmentEnd, sentiment.Start, sentiment.End);
            if (overlap > maxOverlap)
            {
                maxOverlap = overlap;
                matched = sentiment;
            }
        }

        return maxOverlap > 0 || (segmentStart is null && segmentEnd is null);
    }

    private static List<string> GetOverlappingInsightNames(TimeSpan? segmentStart, TimeSpan? segmentEnd, IReadOnlyList<TimedNamedInsight> insights, int maxItems)
    {
        if (insights.Count == 0)
        {
            return [];
        }

        var ranked = insights
            .Select(item => new
            {
                item.Name,
                item.Confidence,
                Overlap = CalculateOverlapSeconds(segmentStart, segmentEnd, item.Start, item.End)
            })
            .Where(static item => item.Overlap > 0 || (item.Overlap == 0 && item.Confidence.HasValue))
            .OrderByDescending(item => item.Overlap)
            .ThenByDescending(item => item.Confidence ?? 0)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToList();

        return ranked;
    }

    private static string? BuildInsightSummary(JsonElement insights, string arrayPropertyName, string textPropertyName, int maxItems)
    {
        if (!insights.TryGetProperty(arrayPropertyName, out var node) || node.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var aggregated = new Dictionary<string, (int Count, double MaxConfidence)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in node.EnumerateArray())
        {
            var name = item.TryGetProperty(textPropertyName, out var textNode) ? textNode.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var confidence = 0d;
            if (item.TryGetProperty("confidence", out var confidenceNode) &&
                confidenceNode.ValueKind == JsonValueKind.Number &&
                confidenceNode.TryGetDouble(out var parsedConfidence))
            {
                confidence = parsedConfidence;
            }

            if (aggregated.TryGetValue(name!, out var existing))
            {
                aggregated[name!] = (existing.Count + 1, Math.Max(existing.MaxConfidence, confidence));
            }
            else
            {
                aggregated[name!] = (1, confidence);
            }
        }

        if (aggregated.Count == 0)
        {
            return null;
        }

        return string.Join(", ",
            aggregated
                .OrderByDescending(static pair => pair.Value.Count)
                .ThenByDescending(static pair => pair.Value.MaxConfidence)
                .ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Take(maxItems)
                .Select(pair =>
                    pair.Value.MaxConfidence > 0
                        ? $"{pair.Key} ({pair.Value.Count}x, {pair.Value.MaxConfidence.ToString("0.##", CultureInfo.InvariantCulture)})"
                        : $"{pair.Key} ({pair.Value.Count}x)"));
    }

    private static string? BuildSentimentSummary(IReadOnlyList<SentimentInsight> sentiments)
    {
        if (sentiments.Count == 0)
        {
            return null;
        }

        var sentimentDurations = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var sentiment in sentiments)
        {
            var key = sentiment.Type;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var duration = CalculateDurationSeconds(sentiment.Start, sentiment.End);
            if (sentimentDurations.TryGetValue(key, out var existing))
            {
                sentimentDurations[key] = existing + duration;
            }
            else
            {
                sentimentDurations[key] = duration;
            }
        }

        if (sentimentDurations.Count == 0)
        {
            return null;
        }

        var total = sentimentDurations.Values.Sum();
        if (total <= 0)
        {
            return string.Join(", ", sentimentDurations.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase));
        }

        return string.Join(", ",
            sentimentDurations
                .OrderByDescending(static pair => pair.Value)
                .Select(pair => $"{pair.Key} ({(pair.Value / total) * 100:0.#}%)"));
    }

    private static bool TryReadSpeakerStatistics(JsonElement insights, out int wordCount, out int fragmentCount, out int longestMonologSeconds)
    {
        wordCount = 0;
        fragmentCount = 0;
        longestMonologSeconds = 0;

        if (!insights.TryGetProperty("statistics", out var statisticsNode) || statisticsNode.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        wordCount = SumNumericObjectValues(statisticsNode, "speakerWordCount");
        fragmentCount = SumNumericObjectValues(statisticsNode, "speakerNumberOfFragments");
        longestMonologSeconds = MaxNumericObjectValue(statisticsNode, "speakerLongestMonolog");

        return true;
    }

    private static int SumNumericObjectValues(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out var valuesNode) || valuesNode.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var total = 0;
        foreach (var property in valuesNode.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number)
            {
                if (property.Value.TryGetInt32(out var intValue))
                {
                    total += intValue;
                    continue;
                }

                if (property.Value.TryGetDouble(out var doubleValue))
                {
                    total += (int)Math.Round(doubleValue, MidpointRounding.AwayFromZero);
                }
            }
        }

        return total;
    }

    private static int MaxNumericObjectValue(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out var valuesNode) || valuesNode.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var maxValue = 0;
        foreach (var property in valuesNode.EnumerateObject())
        {
            var candidate = 0;
            if (property.Value.ValueKind == JsonValueKind.Number)
            {
                if (property.Value.TryGetInt32(out var intValue))
                {
                    candidate = intValue;
                }
                else if (property.Value.TryGetDouble(out var doubleValue))
                {
                    candidate = (int)Math.Round(doubleValue, MidpointRounding.AwayFromZero);
                }
            }

            if (candidate > maxValue)
            {
                maxValue = candidate;
            }
        }

        return maxValue;
    }

    private static double CalculateDurationSeconds(TimeSpan? start, TimeSpan? end)
    {
        if (!start.HasValue || !end.HasValue)
        {
            return 0;
        }

        var duration = end.Value - start.Value;
        return duration > TimeSpan.Zero ? duration.TotalSeconds : 0;
    }

    private static double CalculateOverlapSeconds(TimeSpan? leftStart, TimeSpan? leftEnd, TimeSpan? rightStart, TimeSpan? rightEnd)
    {
        if (leftStart is null && leftEnd is null)
        {
            return rightStart.HasValue || rightEnd.HasValue ? 1 : 0;
        }

        var aStart = leftStart ?? TimeSpan.Zero;
        var aEnd = leftEnd ?? aStart;
        if (aEnd < aStart)
        {
            (aStart, aEnd) = (aEnd, aStart);
        }

        var bStart = rightStart ?? TimeSpan.Zero;
        var bEnd = rightEnd ?? bStart;
        if (bEnd < bStart)
        {
            (bStart, bEnd) = (bEnd, bStart);
        }

        var overlapStart = aStart > bStart ? aStart : bStart;
        var overlapEnd = aEnd < bEnd ? aEnd : bEnd;
        var overlap = overlapEnd - overlapStart;
        return overlap > TimeSpan.Zero ? overlap.TotalSeconds : 0;
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

    private readonly record struct TimedNamedInsight(string Name, TimeSpan? Start, TimeSpan? End, double? Confidence);

    private readonly record struct SentimentInsight(string Type, double? Score, TimeSpan? Start, TimeSpan? End);
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
    private static readonly string[] scopes = new[] { "https://management.azure.com/.default" };

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

        var token = await _credential.GetTokenAsync(new TokenRequestContext(scopes), cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            _cachedToken = token.Token;
            _expiry = token.ExpiresOn;
            return _cachedToken;
        }
    }
}
