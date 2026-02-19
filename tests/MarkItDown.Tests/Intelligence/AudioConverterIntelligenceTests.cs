using MarkItDown;
using MarkItDown.Converters;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using Shouldly;

namespace MarkItDown.Tests.Intelligence;

public class AudioConverterIntelligenceTests
{
    private sealed class StubMetadataExtractor(IReadOnlyDictionary<string, string> metadata) : AudioConverter.IAudioMetadataExtractor
    {
        public Task<IReadOnlyDictionary<string, string>> ExtractAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
        {
            return Task.FromResult(metadata);
        }
    }

    private sealed class StubAudioTranscriber : AudioConverter.IAudioTranscriber
    {
        private readonly string? value;

        public StubAudioTranscriber(string? value = null)
        {
            this.value = value;
        }

        public Task<string?> TranscribeAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
        {
            return Task.FromResult(value);
        }
    }

    private sealed class StubMediaProvider(MediaTranscriptionResult? result) : IMediaTranscriptionProvider
    {
        public Task<MediaTranscriptionResult?> TranscribeAsync(Stream stream, StreamInfo streamInfo, MediaTranscriptionRequest? request = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task ConvertAsync_UsesMediaProviderSegmentsWhenAvailable()
    {
        var metadata = new Dictionary<string, string>
        {
            ["Title"] = "Interview",
            ["Duration"] = "00:02:00"
        };

        var segments = new List<MediaTranscriptSegment>
        {
            new MediaTranscriptSegment("Hello", TimeSpan.Zero, TimeSpan.FromSeconds(30)),
            new MediaTranscriptSegment("World", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60))
        };

        var providerResult = new MediaTranscriptionResult(segments, language: "en-US");

        var converter = new AudioConverter(
            metadataExtractor: new StubMetadataExtractor(metadata),
            transcriber: new StubAudioTranscriber(),
            segmentOptions: SegmentOptions.Default,
            mediaProvider: new StubMediaProvider(providerResult));

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "audio/wav", extension: ".wav", fileName: "sample.wav");

        var result = await converter.ConvertAsync(stream, streamInfo);

        result.Markdown.ShouldContain("Audio Transcript");
        result.Segments.ShouldContain(s => s.Type == SegmentType.Audio && s.Markdown.Contains("Hello"));
        result.Segments.ShouldContain(s => s.Type == SegmentType.Audio && s.Markdown.Contains("World"));
        result.Segments.OfType<DocumentSegment>().Count(s => s.Type == SegmentType.Metadata).ShouldBe(1);
    }

    [Fact]
    public async Task ConvertAsync_VideoInput_UsesProviderTranscriptAndVideoHeading()
    {
        var metadata = new Dictionary<string, string>();
        var providerSegments = new List<MediaTranscriptSegment>
        {
            new MediaTranscriptSegment(
                "Provider transcript text",
                TimeSpan.Zero,
                TimeSpan.FromSeconds(5),
                new Dictionary<string, string>
                {
                    [MetadataKeys.Speaker] = "Speaker #1",
                    [MetadataKeys.Sentiment] = "Neutral",
                    [MetadataKeys.SentimentScore] = "0.5",
                    [MetadataKeys.Topics] = "Health",
                    [MetadataKeys.Keywords] = "AGI",
                    [MetadataKeys.Confidence] = "0.72"
                })
        };

        var providerResult = new MediaTranscriptionResult(
            providerSegments,
            language: "en-US",
            metadata: new Dictionary<string, string>
            {
                [MetadataKeys.Provider] = MetadataValues.ProviderAzureVideoIndexer,
                [MetadataKeys.Language] = "en-US",
                [MetadataKeys.Duration] = "0:00:05",
                [MetadataKeys.Speakers] = "Speaker #1",
                [MetadataKeys.Sentiments] = "Neutral (100%)",
                [MetadataKeys.Topics] = "Health (1x, 1.00)",
                [MetadataKeys.Keywords] = "AGI (1x, 0.95)"
            });

        var converter = new AudioConverter(
            metadataExtractor: new StubMetadataExtractor(metadata),
            transcriber: new StubAudioTranscriber("Local transcript text"),
            segmentOptions: SegmentOptions.Default,
            mediaProvider: new StubMediaProvider(providerResult));

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "video/mp4", extension: ".mp4", fileName: "sample.mp4");

        var result = await converter.ConvertAsync(stream, streamInfo);

        result.Markdown.ShouldContain("### Video Transcript");
        result.Markdown.ShouldContain("[00:00-00:05] Speaker #1");
        result.Markdown.ShouldContain("Emotion/Sentiment: Neutral");
        result.Markdown.ShouldContain("Topics: Health");
        result.Markdown.ShouldContain("Provider transcript text");
        result.Markdown.ShouldNotContain("Local transcript text");
        result.Markdown.ShouldContain("### Video Analysis");
        result.Markdown.ShouldContain("#### Topics");
    }
}
