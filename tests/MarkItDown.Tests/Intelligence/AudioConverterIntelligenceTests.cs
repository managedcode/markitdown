using MarkItDown;
using MarkItDown.Converters;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using Shouldly;

namespace MarkItDown.Tests.Intelligence;

public class AudioConverterIntelligenceTests
{
    private sealed class StubMetadataExtractor : AudioConverter.IAudioMetadataExtractor
    {
        private readonly IReadOnlyDictionary<string, string> metadata;

        public StubMetadataExtractor(IReadOnlyDictionary<string, string> metadata)
        {
            this.metadata = metadata;
        }

        public Task<IReadOnlyDictionary<string, string>> ExtractAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
        {
            return Task.FromResult(metadata);
        }
    }

    private sealed class StubAudioTranscriber : AudioConverter.IAudioTranscriber
    {
        public Task<string?> TranscribeAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class StubMediaProvider : IMediaTranscriptionProvider
    {
        private readonly MediaTranscriptionResult? result;

        public StubMediaProvider(MediaTranscriptionResult? result)
        {
            this.result = result;
        }

        public Task<MediaTranscriptionResult?> TranscribeAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
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
}
