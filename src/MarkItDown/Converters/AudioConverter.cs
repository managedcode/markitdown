using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MarkItDown.Converters;

/// <summary>
/// Converts audio files to Markdown by extracting metadata and optional transcription text.
/// </summary>
public sealed class AudioConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions =
    [
        ".wav",
        ".mp3",
        ".m4a",
        ".mp4",
    ];

    private static readonly HashSet<string> AcceptedMimePrefixes =
    [
        "audio/",
        "video/mp4",
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

    public AudioConverter(
        string? exifToolPath = null,
        Func<byte[], StreamInfo, CancellationToken, Task<string?>>? transcribeAsync = null,
        SegmentOptions? segmentOptions = null)
        : this(
            new ExifToolAudioMetadataExtractor(exifToolPath),
            transcribeAsync is null ? NoOpAudioTranscriber.Instance : new DelegateAudioTranscriber(transcribeAsync),
            segmentOptions)
    {
    }

    internal AudioConverter(IAudioMetadataExtractor metadataExtractor, IAudioTranscriber transcriber, SegmentOptions? segmentOptions = null)
    {
        this.metadataExtractor = metadataExtractor ?? throw new ArgumentNullException(nameof(metadataExtractor));
        this.transcriber = transcriber ?? throw new ArgumentNullException(nameof(transcriber));
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
    }

    public int Priority => 460;

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        => AcceptsInput(streamInfo);

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var extension = streamInfo.Extension?.ToLowerInvariant();
        if (extension is not null && AcceptedExtensions.Contains(extension))
        {
            return true;
        }

        var mime = (streamInfo.MimeType ?? string.Empty).ToLowerInvariant();
        return AcceptedMimePrefixes.Any(mime.StartsWith);
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        var bytes = memory.ToArray();

        var metadata = await metadataExtractor.ExtractAsync(bytes, streamInfo, cancellationToken).ConfigureAwait(false);
        var transcript = await TryTranscribeAsync(bytes, streamInfo, cancellationToken).ConfigureAwait(false);

        var segments = BuildSegments(metadata, transcript, streamInfo);
        var markdown = segments.Count > 0
            ? SegmentMarkdownComposer.Compose(segments, segmentOptions)
            : "*No audio metadata available.*";

        var title = metadata.TryGetValue("Title", out var t) && !string.IsNullOrWhiteSpace(t)
            ? t.Trim()
            : streamInfo.FileName is not null ? Path.GetFileNameWithoutExtension(streamInfo.FileName) : null;

        return new DocumentConverterResult(markdown, title, segments);
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

    private IReadOnlyList<DocumentSegment> BuildSegments(
        IReadOnlyDictionary<string, string> metadata,
        string? transcript,
        StreamInfo streamInfo)
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

        var audioDuration = ParseAudioDuration(metadata);
        var transcriptSegments = CreateAudioSegments(transcript, audioDuration, streamInfo);

        if (transcriptSegments.Count > 0)
        {
            segments.Add(new DocumentSegment(
                markdown: "### Audio Transcript",
                type: SegmentType.Section,
                label: "Audio Transcript",
                source: source));

            segments.AddRange(transcriptSegments);
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
                ["segment"] = (i + 1).ToString(CultureInfo.InvariantCulture),
                ["totalDuration"] = FormatDuration(duration)
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
