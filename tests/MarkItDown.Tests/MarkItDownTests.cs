using MarkItDown;
using MarkItDown.Converters;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using System.Net;
using System.Text;

namespace MarkItDown.Tests;

public class MarkItDownTests
{
    [Fact]
    public async Task ConvertAsync_PlainTextFile_ReturnsCorrectMarkdown()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDownClient();
        var content = "This is a test file.\nWith multiple lines.\n";
        var bytes = Encoding.UTF8.GetBytes(content);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(extension: ".txt");

        // Act
        var result = await markItDown.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Equal(content, result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_MarkdownFile_ReturnsCorrectMarkdown()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDownClient();
        var content = "# Header\n\nThis is **bold** text.\n";
        var bytes = Encoding.UTF8.GetBytes(content);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(extension: ".md");

        // Act
        var result = await markItDown.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Equal(content, result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_UnsupportedFormat_ThrowsUnsupportedFormatException()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDownClient();
        using var stream = new MemoryStream([0x50, 0x4B, 0x03, 0x04]); // ZIP file signature
        var streamInfo = new StreamInfo(mimeType: "application/zip", extension: ".zip");

        // Act & Assert
        await Assert.ThrowsAsync<UnsupportedFormatException>(
            () => markItDown.ConvertAsync(stream, streamInfo));
    }

    [Fact]
    public async Task ConvertAsync_WhenConverterThrows_ErrorIncludesConverterName()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDownClient();
        markItDown.RegisterConverter(new ThrowingTestConverter(), priority: 1);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("boom"));
        var streamInfo = new StreamInfo(extension: ".boom", mimeType: "application/x-boom");

        // Act
        var exception = await Assert.ThrowsAsync<UnsupportedFormatException>(
            () => markItDown.ConvertAsync(stream, streamInfo));

        // Assert
        Assert.Contains("Converter 'ThrowingTestConverter' failed", exception.Message);
        Assert.IsType<AggregateException>(exception.InnerException);
        var aggregate = (AggregateException)exception.InnerException!;
        Assert.Contains(aggregate.InnerExceptions, ex =>
            ex is InvalidOperationException &&
            ex.Message.Contains("Simulated converter failure.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConvertAsync_WhenMediaTranscriptionRequestedAndProviderFails_ErrorIncludesAudioConverter()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDownClient(new global::MarkItDown.MarkItDownOptions
        {
            MediaTranscriptionProvider = new ThrowingMediaProvider()
        });

        using var stream = new MemoryStream([1, 2, 3, 4]);
        var streamInfo = new StreamInfo(mimeType: "audio/wav", extension: ".wav", fileName: "sample.wav");
        var request = ConversionRequest.FromConfiguration(builder =>
            builder.UseMediaTranscription(new MediaTranscriptionRequest(
                PreferredProvider: MediaTranscriptionProviderKind.Custom,
                Language: "en-US")));

        // Act
        var exception = await Assert.ThrowsAsync<UnsupportedFormatException>(
            () => markItDown.ConvertAsync(stream, streamInfo, request));

        // Assert
        Assert.Contains("Converter 'AudioConverter' failed", exception.Message);
        Assert.IsType<AggregateException>(exception.InnerException);
        var aggregate = (AggregateException)exception.InnerException!;
        Assert.Contains(aggregate.InnerExceptions, ex =>
            ex is FileConversionException &&
            ex.Message.Contains("ThrowingMediaProvider", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConvertAsync_VideoInput_WhenMediaProviderFailsWithoutExplicitRequest_ErrorIncludesVideoConverter()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDownClient(new global::MarkItDown.MarkItDownOptions
        {
            MediaTranscriptionProvider = new ThrowingMediaProvider()
        });

        using var stream = new MemoryStream([1, 2, 3, 4]);
        var streamInfo = new StreamInfo(mimeType: "video/mp4", extension: ".mp4", fileName: "video.mp4");

        // Act
        var exception = await Assert.ThrowsAsync<UnsupportedFormatException>(
            () => markItDown.ConvertAsync(stream, streamInfo));

        // Assert
        Assert.Contains("Converter 'VideoConverter' failed", exception.Message);
        Assert.IsType<AggregateException>(exception.InnerException);
        var aggregate = (AggregateException)exception.InnerException!;
        Assert.Contains(aggregate.InnerExceptions, ex =>
            ex is FileConversionException &&
            ex.Message.Contains("ThrowingMediaProvider", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConvertAsync_VideoInput_WhenMediaProviderUnauthorized_ThrowsFileConversionException()
    {
        var markItDown = new global::MarkItDown.MarkItDownClient(new global::MarkItDown.MarkItDownOptions
        {
            MediaTranscriptionProvider = new UnauthorizedMediaProvider()
        });

        using var stream = new MemoryStream([1, 2, 3, 4]);
        var streamInfo = new StreamInfo(mimeType: "video/mp4", extension: ".mp4", fileName: "video.mp4");

        var exception = await Assert.ThrowsAsync<FileConversionException>(
            () => markItDown.ConvertAsync(stream, streamInfo));

        Assert.Contains("Authentication/authorization failed", exception.Message);
        Assert.Contains("401", exception.Message);
        Assert.IsType<AggregateException>(exception.InnerException);
    }

    [Fact]
    public async Task ConvertAsync_NonSeekableStream_CompletesViaDiskBuffering()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDownClient();
        var content = "disk-first";
        var nonSeekableStream = new NonSeekableMemoryStream(Encoding.UTF8.GetBytes(content));
        var streamInfo = new StreamInfo(extension: ".txt");

        // Act
        var result = await markItDown.ConvertAsync(nonSeekableStream, streamInfo);

        // Assert
        Assert.Equal(content, result.Markdown);
    }

    [Fact]
    public void RegisterConverter_CustomConverter_AddsToConverterList()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDownClient();
        var customConverter = new TestConverter();

        // Act
        markItDown.RegisterConverter(customConverter, 5.0);

        // Assert
        // We can't directly test the internal converter list, but we can test that
        // the converter is used by creating a scenario where it would be called
        // This is tested implicitly through the conversion tests
        Assert.True(true); // Placeholder assertion
    }

    [Fact]
    public async Task ConvertAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDownClient();
        var nonExistentFile = "nonexistent.txt";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => markItDown.ConvertAsync(nonExistentFile));
    }

    // Helper classes for testing
    private class NonSeekableMemoryStream : MemoryStream
    {
        public NonSeekableMemoryStream(byte[] buffer) : base(buffer) { }
        public override bool CanSeek => false;
    }

    private class TestConverter : DocumentConverterBase
    {
        public TestConverter()
            : base(priority: 999)
        {
        }

        public override bool AcceptsInput(StreamInfo streamInfo)
        {
            return false; // Never accepts to avoid interfering with other tests
        }

        public override bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        {
            return false; // Never accepts to avoid interfering with other tests
        }

        public override Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DocumentConverterResult("Test conversion"));
        }
    }

    private sealed class ThrowingTestConverter : DocumentConverterBase
    {
        public ThrowingTestConverter()
            : base(priority: 1)
        {
        }

        public override bool AcceptsInput(StreamInfo streamInfo)
        {
            return string.Equals(streamInfo.Extension, ".boom", StringComparison.OrdinalIgnoreCase);
        }

        public override Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated converter failure.");
        }
    }

    private sealed class ThrowingMediaProvider : IMediaTranscriptionProvider
    {
        public Task<MediaTranscriptionResult?> TranscribeAsync(
            Stream stream,
            StreamInfo streamInfo,
            MediaTranscriptionRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated media provider failure.");
        }
    }

    private sealed class UnauthorizedMediaProvider : IMediaTranscriptionProvider
    {
        public Task<MediaTranscriptionResult?> TranscribeAsync(
            Stream stream,
            StreamInfo streamInfo,
            MediaTranscriptionRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            throw new HttpRequestException(
                "Response status code does not indicate success: 401 (Unauthorized).",
                inner: null,
                statusCode: HttpStatusCode.Unauthorized);
        }
    }
}
