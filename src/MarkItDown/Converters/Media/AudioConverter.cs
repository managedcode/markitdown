using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;

namespace MarkItDown.Converters;

/// <summary>
/// Converts audio files to Markdown by extracting metadata and optional transcription text.
/// </summary>
public sealed class AudioConverter : DocumentPipelineConverterBase
{
    private static readonly HashSet<string> AcceptedExtensions =
    [
        ".wav",
        ".mp3",
        ".m4a",
    ];

    private static readonly HashSet<string> AcceptedMimePrefixes =
    [
        "audio/",
    ];

    private static readonly string[] MetadataFields =
    {
        "Title",
        "Artist",
        "Author",
        "Band",
        "Album",
        "Genre",
        "Track",
        "DateTimeOriginal",
        "CreateDate",
        "Duration",
        "NumChannels",
        "SampleRate",
        "AvgBytesPerSec",
        "BitsPerSample",
    };

    private readonly IAudioMetadataExtractor metadataExtractor;
    private readonly IAudioTranscriber transcriber;
    private readonly SegmentOptions segmentOptions;
    private readonly IMediaTranscriptionProvider? mediaProvider;
    private static readonly string[] prefixes = new[] { "video/" };

    public AudioConverter(
        string? exifToolPath = null,
        Func<byte[], StreamInfo, CancellationToken, Task<string?>>? transcribeAsync = null,
        SegmentOptions? segmentOptions = null,
        IMediaTranscriptionProvider? mediaProvider = null)
        : this(
            new ExifToolAudioMetadataExtractor(exifToolPath),
            transcribeAsync is null ? NoOpAudioTranscriber.Instance : new DelegateAudioTranscriber(transcribeAsync),
            segmentOptions,
            mediaProvider)
    {
    }

    internal AudioConverter(
        IAudioMetadataExtractor metadataExtractor,
        IAudioTranscriber transcriber,
        SegmentOptions? segmentOptions = null,
        IMediaTranscriptionProvider? mediaProvider = null)
        : base(priority: 460)
    {
        this.metadataExtractor = metadataExtractor ?? throw new ArgumentNullException(nameof(metadataExtractor));
        this.transcriber = transcriber ?? throw new ArgumentNullException(nameof(transcriber));
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
        this.mediaProvider = mediaProvider;
    }

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var extension = streamInfo.Extension?.ToLowerInvariant();
        if (extension is not null && AcceptedExtensions.Contains(extension))
        {
            return true;
        }

        var mime = (streamInfo.MimeType ?? string.Empty).ToLowerInvariant();
        return AcceptedMimePrefixes.Any(mime.StartsWith);
    }

    public override async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(streamInfo);

        var context = ConversionContextAccessor.Current;
        var mediaRequest = context?.Request.Intelligence.Media;
        var isVideoInput = IsVideoInput(streamInfo);

        await using var source = await MaterializeSourceAsync(stream, streamInfo, ResolveDefaultExtension(streamInfo), cancellationToken).ConfigureAwait(false);
        var filePath = source.FilePath;

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);

        var metadata = await metadataExtractor.ExtractAsync(bytes, streamInfo, cancellationToken).ConfigureAwait(false);
        var transcript = isVideoInput
            ? null
            : await TryTranscribeAsync(bytes, streamInfo, cancellationToken).ConfigureAwait(false);
        var providerTranscript = await TryTranscribeWithProviderAsync(filePath, streamInfo, mediaRequest, cancellationToken).ConfigureAwait(false);

        var transcriptText = isVideoInput
            ? providerTranscript?.GetFullTranscript()
            : !string.IsNullOrWhiteSpace(transcript)
                ? transcript
                : providerTranscript?.GetFullTranscript();

        var titleHint = metadata.TryGetValue("Title", out var rawTitle) && !string.IsNullOrWhiteSpace(rawTitle)
            ? rawTitle.Trim()
            : null;

        var segments = BuildSegments(metadata, transcriptText, streamInfo, providerTranscript).ToList();
        if (IsMediaTranscriptRequired(streamInfo, mediaRequest) && !segments.Any(static segment => segment.Type == SegmentType.Audio))
        {
            throw new FileConversionException($"Media transcription did not produce transcript segments for '{DescribeInput(streamInfo)}'.");
        }

        if (segments.Count == 0)
        {
            segments.Add(new DocumentSegment(
                markdown: "*No audio metadata available.*",
                type: SegmentType.Metadata,
                label: "Summary",
                source: streamInfo.FileName));
        }

        var artifacts = new ConversionArtifacts();
        foreach (var segment in segments)
        {
            artifacts.TextBlocks.Add(new TextArtifact(segment.Markdown, segment.Number, streamInfo.FileName, segment.Label));
        }

        var meta = SegmentMarkdownComposer.Compose(segments, artifacts, streamInfo, segmentOptions, titleHint);
        var markdown = meta.Markdown;

        var normalizedMetaTitle = NormalizeTrackTitle(meta.Title);
        var normalizedMetadataTitle = NormalizeTrackTitle(titleHint);

        if (!string.IsNullOrWhiteSpace(normalizedMetaTitle) &&
            normalizedMetaTitle.StartsWith("*No audio metadata", StringComparison.OrdinalIgnoreCase))
        {
            normalizedMetaTitle = null;
        }

        var title = normalizedMetaTitle
            ?? normalizedMetadataTitle
            ?? (streamInfo.FileName is not null ? Path.GetFileNameWithoutExtension(streamInfo.FileName) : null);

        return new DocumentConverterResult(markdown, title, segments, artifacts);
    }

    private async Task<string?> TryTranscribeAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        try
        {
            return await transcriber.TranscribeAsync(audioBytes, streamInfo, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task<MediaTranscriptionResult?> TryTranscribeWithProviderAsync(string filePath, StreamInfo streamInfo, MediaTranscriptionRequest? request, CancellationToken cancellationToken)
    {
        var provider = ConversionContextAccessor.Current?.Providers.Media ?? mediaProvider;
        var requiresExplicitProvider = request is not null;
        var isVideoInput = IsVideoInput(streamInfo);
        var failOnProviderError = requiresExplicitProvider || isVideoInput;

        if (provider is null)
        {
            if (requiresExplicitProvider || isVideoInput)
            {
                throw new FileConversionException("Media transcription was requested, but no media transcription provider is configured.");
            }

            return null;
        }

        try
        {
            await using var providerStream = OpenReadOnlyFile(filePath);
            var result = await provider.TranscribeAsync(providerStream, streamInfo, request, cancellationToken).ConfigureAwait(false);
            if (requiresExplicitProvider && (result is null || result.Segments.Count == 0))
            {
                throw new FileConversionException($"Media transcription provider '{provider.GetType().Name}' returned no transcript segments.");
            }

            return result;
        }
        catch (Exception ex) when (ex is not MarkItDownException)
        {
            if (failOnProviderError)
            {
                throw new FileConversionException($"Media transcription provider '{provider.GetType().Name}' failed: {ex.Message}", ex);
            }

            return null;
        }
    }

    private static bool IsMediaTranscriptRequired(StreamInfo streamInfo, MediaTranscriptionRequest? request)
        => request is not null || IsVideoInput(streamInfo);

    private static bool IsVideoInput(StreamInfo streamInfo)
        => streamInfo.MatchesMime(prefixes);

    private static string DescribeInput(StreamInfo streamInfo)
        => streamInfo.FileName
           ?? streamInfo.LocalPath
           ?? streamInfo.Url
           ?? streamInfo.Extension
           ?? "input";

    private IReadOnlyList<DocumentSegment> BuildSegments(
        IReadOnlyDictionary<string, string> metadata,
        string? transcript,
        StreamInfo streamInfo,
        MediaTranscriptionResult? providerResult)
    {
        var segments = new List<DocumentSegment>();
        var source = streamInfo.FileName;

        var metadataMarkdown = BuildMetadataMarkdown(metadata);
        if (!string.IsNullOrWhiteSpace(metadataMarkdown))
        {
            segments.Add(new DocumentSegment(
                markdown: metadataMarkdown,
                type: SegmentType.Metadata,
                label: "Metadata",
                source: source));
        }

        IReadOnlyList<DocumentSegment> transcriptSegments;
        if (providerResult is not null && providerResult.Segments.Count > 0)
        {
            transcriptSegments = CreateAudioSegments(providerResult, streamInfo);
        }
        else
        {
            var audioDuration = ParseAudioDuration(metadata);
            transcriptSegments = CreateAudioSegments(transcript, audioDuration, streamInfo);
        }

        if (transcriptSegments.Count > 0)
        {
            var transcriptLabel = IsVideoInput(streamInfo) ? "Video Transcript" : "Audio Transcript";
            segments.Add(new DocumentSegment(
                markdown: $"### {transcriptLabel}",
                type: SegmentType.Section,
                label: transcriptLabel,
                source: source));

            segments.AddRange(transcriptSegments);
        }

        if (providerResult is not null && IsVideoInput(streamInfo))
        {
            var analysisMarkdown = BuildVideoAnalysisMarkdown(providerResult);
            if (!string.IsNullOrWhiteSpace(analysisMarkdown))
            {
                segments.Add(new DocumentSegment(
                    markdown: analysisMarkdown,
                    type: SegmentType.Metadata,
                    label: "Video Analysis",
                    source: source));
            }
        }

        return segments;
    }

    private static string BuildMetadataMarkdown(IReadOnlyDictionary<string, string> metadata)
    {
        var builder = new StringBuilder();

        foreach (var field in MetadataFields)
        {
            if (metadata.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                builder.Append(field).Append(':').Append(' ').AppendLine(value.Trim());
            }
        }

        return builder.ToString().TrimEnd();
    }

    private IReadOnlyList<DocumentSegment> CreateAudioSegments(string? transcript, TimeSpan? totalDuration, StreamInfo streamInfo)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return Array.Empty<DocumentSegment>();
        }

        var cleanedTranscript = transcript.Trim();
        var duration = totalDuration.GetValueOrDefault();
        var segmentDuration = segmentOptions.Audio.SegmentDuration;

        if (!totalDuration.HasValue || duration <= TimeSpan.Zero || segmentDuration <= TimeSpan.Zero)
        {
            var metadata = totalDuration.HasValue
                ? new Dictionary<string, string> { ["totalDuration"] = FormatDuration(duration) }
                : null;

            return new List<DocumentSegment>
            {
                new DocumentSegment(
                    markdown: cleanedTranscript,
                    type: SegmentType.Audio,
                    number: 1,
                    label: "Segment 1",
                    startTime: totalDuration.HasValue ? TimeSpan.Zero : null,
                    endTime: totalDuration,
                    source: streamInfo.FileName,
                    additionalMetadata: metadata)
            };
        }

        var segmentCount = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds / segmentDuration.TotalSeconds));
        var lengthBasedCap = Math.Max(1, cleanedTranscript.Length / 500);
        segmentCount = Math.Min(segmentCount, lengthBasedCap);

        var chunks = SplitTranscriptIntoChunks(cleanedTranscript, segmentCount);
        var segments = new List<DocumentSegment>(chunks.Count);

        for (var i = 0; i < chunks.Count; i++)
        {
            var start = segmentDuration * i;
            if (start > duration)
            {
                start = duration;
            }

            var end = segmentDuration * (i + 1);
            if (end > duration)
            {
                end = duration;
            }

            if (i == chunks.Count - 1 && end < duration)
            {
                end = duration;
            }

            var metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Segment] = (i + 1).ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.TotalDuration] = FormatDuration(duration)
            };

            segments.Add(new DocumentSegment(
                markdown: chunks[i],
                type: SegmentType.Audio,
                number: i + 1,
                label: $"Segment {i + 1}",
                startTime: start,
                endTime: end,
                source: streamInfo.FileName,
                additionalMetadata: metadata));
        }

        return segments;
    }

    private static IReadOnlyList<DocumentSegment> CreateAudioSegments(MediaTranscriptionResult mediaResult, StreamInfo streamInfo)
    {
        if (mediaResult.Segments.Count == 0)
        {
            return Array.Empty<DocumentSegment>();
        }

        var isVideoInput = IsVideoInput(streamInfo);
        var segments = new List<DocumentSegment>(mediaResult.Segments.Count);
        for (var i = 0; i < mediaResult.Segments.Count; i++)
        {
            var item = mediaResult.Segments[i];
            var metadata = new Dictionary<string, string>(item.Metadata)
            {
                [MetadataKeys.Segment] = (i + 1).ToString(CultureInfo.InvariantCulture)
            };

            segments.Add(new DocumentSegment(
                markdown: isVideoInput ? FormatVideoTranscriptSegment(item) : item.Text,
                type: SegmentType.Audio,
                number: i + 1,
                label: $"Segment {i + 1}",
                startTime: item.Start,
                endTime: item.End,
                source: streamInfo.FileName,
                additionalMetadata: metadata));
        }

        return segments;
    }

    private static string FormatVideoTranscriptSegment(MediaTranscriptSegment item)
    {
        var builder = new StringBuilder();
        var hasHeader = false;

        var range = FormatRange(item.Start, item.End);
        var speaker = ResolveSpeaker(item.Metadata);
        if (!string.IsNullOrWhiteSpace(range) || !string.IsNullOrWhiteSpace(speaker))
        {
            builder.Append("**");
            if (!string.IsNullOrWhiteSpace(range))
            {
                builder.Append('[').Append(range).Append(']').Append(' ');
            }

            builder.Append(string.IsNullOrWhiteSpace(speaker) ? "Speaker" : speaker);
            builder.AppendLine("**");
            hasHeader = true;
        }

        AppendDetailLine(builder, "Emotion/Sentiment", TryGetValue(item.Metadata, MetadataKeys.Sentiment));

        var sentimentScore = TryGetValue(item.Metadata, MetadataKeys.SentimentScore);
        if (!string.IsNullOrWhiteSpace(sentimentScore))
        {
            AppendDetailLine(builder, "Sentiment Score", sentimentScore);
        }

        var confidence = TryGetValue(item.Metadata, MetadataKeys.Confidence);
        if (!string.IsNullOrWhiteSpace(confidence))
        {
            AppendDetailLine(builder, "Transcript Confidence", confidence);
        }

        AppendDetailLine(builder, "Topics", TryGetValue(item.Metadata, MetadataKeys.Topics));
        AppendDetailLine(builder, "Keywords", TryGetValue(item.Metadata, MetadataKeys.Keywords));

        var text = item.Text?.Trim() ?? string.Empty;
        if (builder.Length > 0 && text.Length > 0)
        {
            if (hasHeader || builder.ToString().Contains("\n- ", StringComparison.Ordinal))
            {
                builder.AppendLine();
            }
        }

        builder.Append(text);
        return builder.ToString().Trim();
    }

    private static string? BuildVideoAnalysisMarkdown(MediaTranscriptionResult result)
    {
        if (!result.Metadata.TryGetValue(MetadataKeys.Provider, out var provider) ||
            !string.Equals(provider, MetadataValues.ProviderAzureVideoIndexer, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var builder = new StringBuilder();
        var hasContent = false;

        builder.AppendLine("### Video Analysis");
        builder.AppendLine();

        var overview = new List<(string Label, string? Value)>
        {
            ("Video Indexer State", TryGetValue(result.Metadata, MetadataKeys.VideoIndexerState)),
            ("Video Indexer Index Id", TryGetValue(result.Metadata, MetadataKeys.VideoIndexerIndexId)),
            ("Video Indexer Progress", TryGetValue(result.Metadata, MetadataKeys.VideoIndexerProgress)),
            ("Language", TryGetValue(result.Metadata, MetadataKeys.Language)),
            ("Duration", TryGetValue(result.Metadata, MetadataKeys.Duration)),
            ("Speakers", TryGetValue(result.Metadata, MetadataKeys.Speakers)),
            ("Speaker Count", TryGetValue(result.Metadata, MetadataKeys.SpeakerCount)),
            ("Total Word Count", TryGetValue(result.Metadata, MetadataKeys.WordCount)),
            ("Speech Fragments", TryGetValue(result.Metadata, MetadataKeys.FragmentCount)),
            ("Longest Monologue (s)", TryGetValue(result.Metadata, MetadataKeys.LongestMonologSeconds))
        };

        var overviewLines = overview.Where(static item => !string.IsNullOrWhiteSpace(item.Value)).ToList();
        if (overviewLines.Count > 0)
        {
            builder.AppendLine("#### Overview");
            foreach (var (label, value) in overviewLines)
            {
                builder.Append("- **").Append(label).Append(":** ").AppendLine(value);
            }

            builder.AppendLine();
            hasContent = true;
        }

        hasContent |= AppendAnalysisSection(builder, "Emotion / Sentiment", TryGetValue(result.Metadata, MetadataKeys.Sentiments));
        hasContent |= AppendAnalysisSection(builder, "Topics", TryGetValue(result.Metadata, MetadataKeys.Topics));
        hasContent |= AppendAnalysisSection(builder, "Keywords", TryGetValue(result.Metadata, MetadataKeys.Keywords));
        hasContent |= AppendAnalysisSection(builder, "Visual Labels", TryGetValue(result.Metadata, MetadataKeys.Labels));
        hasContent |= AppendAnalysisSection(builder, "Named Locations", TryGetValue(result.Metadata, MetadataKeys.NamedLocations));

        if (!hasContent)
        {
            return null;
        }

        return builder.ToString().TrimEnd();
    }

    private static bool AppendAnalysisSection(StringBuilder builder, string title, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        builder.Append("#### ").AppendLine(title);
        builder.Append("- ").AppendLine(value);
        builder.AppendLine();
        return true;
    }

    private static void AppendDetailLine(StringBuilder builder, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append("- ").Append(label).Append(": ").AppendLine(value);
    }

    private static string? ResolveSpeaker(IReadOnlyDictionary<string, string> metadata)
    {
        var speaker = TryGetValue(metadata, MetadataKeys.Speaker);
        if (!string.IsNullOrWhiteSpace(speaker))
        {
            return speaker;
        }

        var speakerId = TryGetValue(metadata, MetadataKeys.SpeakerId);
        if (string.IsNullOrWhiteSpace(speakerId))
        {
            return null;
        }

        return $"Speaker #{speakerId}";
    }

    private static string? FormatRange(TimeSpan? start, TimeSpan? end)
    {
        if (!start.HasValue && !end.HasValue)
        {
            return null;
        }

        if (start.HasValue && end.HasValue)
        {
            return $"{FormatDuration(start.Value)}-{FormatDuration(end.Value)}";
        }

        if (start.HasValue)
        {
            return $"{FormatDuration(start.Value)}-?";
        }

        return $"?-{FormatDuration(end!.Value)}";
    }

    private static string? TryGetValue(IReadOnlyDictionary<string, string> metadata, string key)
        => metadata.TryGetValue(key, out var value) ? value : null;

    private static List<string> SplitTranscriptIntoChunks(string transcript, int segmentCount)
    {
        if (segmentCount <= 1)
        {
            return new List<string> { transcript };
        }

        var segments = new List<string>(segmentCount);
        var length = transcript.Length;
        var chunkSize = Math.Max(1, (int)Math.Ceiling((double)length / segmentCount));
        var position = 0;

        var breakChars = new[] { '.', '!', '?', '\n', '\r', ' ' };

        while (position < length)
        {
            var end = Math.Min(position + chunkSize, length);
            if (end < length)
            {
                var searchLength = Math.Min(chunkSize, 200);
                var splitIndex = transcript.LastIndexOfAny(breakChars, end - 1, searchLength);
                if (splitIndex > position)
                {
                    end = splitIndex + 1;
                }
            }

            var chunk = transcript[position..end].Trim();
            if (!string.IsNullOrEmpty(chunk))
            {
                segments.Add(chunk);
            }

            position = end;
        }

        if (segments.Count == 0)
        {
            segments.Add(transcript);
        }

        return segments;
    }

    private static TimeSpan? ParseAudioDuration(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("Duration", out var value) && TryParseDuration(value, out var duration))
        {
            return duration;
        }

        if (metadata.TryGetValue("MediaDuration", out value) && TryParseDuration(value, out duration))
        {
            return duration;
        }

        return null;
    }

    private static bool TryParseDuration(string rawValue, out TimeSpan duration)
    {
        var value = rawValue.Trim();
        var colonCount = 0;
        foreach (var ch in value)
        {
            if (ch == ':')
            {
                colonCount++;
            }
        }

        if (colonCount == 1 && TimeSpan.TryParseExact(value, @"mm\:ss", CultureInfo.InvariantCulture, out duration))
        {
            return true;
        }

        if (colonCount == 2 && TimeSpan.TryParseExact(value, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out duration))
        {
            return true;
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out duration))
        {
            return true;
        }

        var sanitized = value.Replace(',', '.');
        if (sanitized.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            sanitized = sanitized[..^1];
        }

        if (double.TryParse(sanitized, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            duration = TimeSpan.FromSeconds(seconds);
            return true;
        }

        duration = default;
        return false;
    }

    private static string FormatDuration(TimeSpan duration)
        => duration.ToString(duration.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);

    private static string ResolveDefaultExtension(StreamInfo streamInfo)
        => string.IsNullOrWhiteSpace(streamInfo.Extension) ? ".wav" : streamInfo.Extension!;

    internal interface IAudioMetadataExtractor
    {
        Task<IReadOnlyDictionary<string, string>> ExtractAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken);
    }

    internal interface IAudioTranscriber
    {
        Task<string?> TranscribeAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken);
    }

    private sealed class ExifToolAudioMetadataExtractor : IAudioMetadataExtractor
    {
        private readonly string? exifToolPath;

        public ExifToolAudioMetadataExtractor(string? exifToolPath)
        {
            this.exifToolPath = exifToolPath;
        }

        public async Task<IReadOnlyDictionary<string, string>> ExtractAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
        {
            var result = await ExifToolMetadataExtractor
                .ExtractAsync(audioBytes, streamInfo.Extension, exifToolPath, cancellationToken)
                .ConfigureAwait(false);

            return result;
        }
    }

    private static string? NormalizeTrackTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            var parts = trimmed.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0].Equals("Title", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = parts[1];
            }
        }

        return trimmed.Length == 0 ? null : trimmed;
    }

    private sealed class DelegateAudioTranscriber : IAudioTranscriber
    {
        private readonly Func<byte[], StreamInfo, CancellationToken, Task<string?>> factory;

        public DelegateAudioTranscriber(Func<byte[], StreamInfo, CancellationToken, Task<string?>> factory)
            => this.factory = factory;

        public Task<string?> TranscribeAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
            => factory(audioBytes, streamInfo, cancellationToken);
    }

    private sealed class NoOpAudioTranscriber : IAudioTranscriber
    {
        public static NoOpAudioTranscriber Instance { get; } = new();

        private NoOpAudioTranscriber()
        {
        }

        public Task<string?> TranscribeAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);
    }
}
