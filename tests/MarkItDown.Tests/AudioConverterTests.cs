using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;
using MarkItDown.Converters;
using Xunit;

namespace MarkItDown.Tests;

public class AudioConverterTests
{
    [Fact]
    public async Task ConvertAsync_MetadataAndTranscript_ProducesExpectedMarkdown()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            ["Title"] = "Test Track",
            ["Artist"] = "The Artist",
            ["Duration"] = "03:00"
        };

        var converter = new AudioConverter(
            metadataExtractor: new StubAudioMetadataExtractor(metadata),
            transcriber: new StubAudioTranscriber("Hello world transcript."));

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "audio/mpeg", extension: ".mp3", fileName: "track.mp3");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("title: \"Test Track\"", result.Markdown);
        Assert.Contains("Title: Test Track", result.Markdown);
        Assert.Contains("Artist: The Artist", result.Markdown);
        Assert.Contains("### Audio Transcript", result.Markdown);
        Assert.Contains("Hello world transcript.", result.Markdown);
        Assert.Equal("Test Track", result.Title);
        Assert.Equal(SegmentType.Metadata, result.Segments[0].Type);
        Assert.Equal(SegmentType.Section, result.Segments[1].Type);
        Assert.Equal(SegmentType.Audio, result.Segments[2].Type);
        Assert.Equal(TimeSpan.Zero, result.Segments[2].StartTime);
        Assert.Equal(TimeSpan.FromMinutes(3), result.Segments[2].EndTime);
    }

    [Fact]
    public async Task ConvertAsync_NoMetadata_NoTranscript_UsesDefaultMessage()
    {
        // Arrange
        var converter = new AudioConverter(
            metadataExtractor: new StubAudioMetadataExtractor(new Dictionary<string, string>()),
            transcriber: new StubAudioTranscriber(null));

        using var stream = new MemoryStream(new byte[] { 9, 9, 9 });
        var streamInfo = new StreamInfo(mimeType: "audio/wav", extension: ".wav", fileName: "sample.wav");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.StartsWith("---", result.Markdown);
        Assert.Contains("*No audio metadata available.*", result.Markdown);
        Assert.Equal("sample", result.Title);
    }

    [Theory]
    [InlineData(".mp3", "audio/mpeg", true)]
    [InlineData(".wav", "audio/wav", true)]
    [InlineData(".txt", "text/plain", false)]
    public void AcceptsInput_VariousExtensions_ReturnsExpected(string extension, string mime, bool expected)
    {
        // Arrange
        var converter = new AudioConverter(
            metadataExtractor: new StubAudioMetadataExtractor(new Dictionary<string, string>()),
            transcriber: new StubAudioTranscriber(null));

        var streamInfo = new StreamInfo(mimeType: mime, extension: extension);

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.Equal(expected, result);
    }

    private sealed class StubAudioMetadataExtractor : AudioConverter.IAudioMetadataExtractor
    {
        private readonly IReadOnlyDictionary<string, string> metadata;

        public StubAudioMetadataExtractor(IReadOnlyDictionary<string, string> metadata)
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
        private readonly string? transcript;

        public StubAudioTranscriber(string? transcript)
        {
            this.transcript = transcript;
        }

        public Task<string?> TranscribeAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
        {
            return Task.FromResult(transcript);
        }
    }
}
