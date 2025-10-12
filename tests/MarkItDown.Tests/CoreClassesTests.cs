using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using MarkItDown;
using MarkItDown.Converters;

namespace MarkItDown.Tests;

public class StreamInfoTests
{
    [Fact]
    public void Constructor_WithAllParameters_SetsPropertiesCorrectly()
    {
        // Arrange
        var mimeType = "text/plain";
        var extension = ".txt";
        var charset = Encoding.UTF8;
        var filename = "test.txt";
        var url = "https://example.com/test.txt";

        // Act
        var streamInfo = new StreamInfo(mimeType, extension, charset, filename, localPath: null, url);

        // Assert
        Assert.Equal(mimeType, streamInfo.MimeType);
        Assert.Equal(extension, streamInfo.Extension);
        Assert.Equal(charset, streamInfo.Charset);
        Assert.Equal(filename, streamInfo.FileName);
        Assert.Equal(url, streamInfo.Url);
    }

    [Fact]
    public void Constructor_WithExtensionWithoutDot_NormalizesExtension()
    {
        // Arrange
        var extension = "txt";

        // Act
        var streamInfo = new StreamInfo(extension: extension);

        // Assert
        Assert.Equal(".txt", streamInfo.Extension);
    }

    [Fact]
    public void Constructor_WithExtensionWithDot_KeepsExtension()
    {
        // Arrange
        var extension = ".txt";

        // Act
        var streamInfo = new StreamInfo(extension: extension);

        // Assert
        Assert.Equal(".txt", streamInfo.Extension);
    }

    [Fact]
    public void Constructor_WithNullExtension_KeepsNull()
    {
        // Act
        var streamInfo = new StreamInfo(extension: null);

        // Assert
        Assert.Null(streamInfo.Extension);
    }

    [Fact]
    public void Constructor_WithEmptyExtension_ReturnsNull()
    {
        // Act
        var streamInfo = new StreamInfo(extension: "");

        // Assert
        Assert.Null(streamInfo.Extension);
    }

    [Fact]
    public void Constructor_WithWhitespaceExtension_ReturnsNull()
    {
        // Act
        var streamInfo = new StreamInfo(extension: "   ");

        // Assert
        Assert.Null(streamInfo.Extension);
    }

    [Theory]
    [InlineData("TXT", ".txt")]
    [InlineData("MD", ".md")]
    [InlineData("PDF", ".pdf")]
    public void Constructor_WithUppercaseExtension_NormalizesToLowercase(string input, string expected)
    {
        // Act
        var streamInfo = new StreamInfo(extension: input);

        // Assert
        Assert.Equal(expected, streamInfo.Extension);
    }
}

public class DocumentConverterResultTests
{
    [Fact]
    public void Constructor_WithMarkdown_SetsMarkdownProperty()
    {
        // Arrange
        var markdown = "# Test\n\nThis is a test.";

        // Act
        var result = new DocumentConverterResult(markdown);

        // Assert
        Assert.Equal(markdown, result.Markdown);
        Assert.Null(result.Title);
    }

    [Fact]
    public void Constructor_WithMarkdownAndTitle_SetsBothProperties()
    {
        // Arrange
        var markdown = "# Test\n\nThis is a test.";
        var title = "Test Document";

        // Act
        var result = new DocumentConverterResult(markdown, title);

        // Assert
        Assert.Equal(markdown, result.Markdown);
        Assert.Equal(title, result.Title);
    }

    [Fact]
    public void Constructor_WithNullMarkdown_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DocumentConverterResult(null!));
    }

    [Fact]
    public void ToString_ReturnsMarkdown()
    {
        // Arrange
        var markdown = "# Test\n\nThis is a test.";
        var result = new DocumentConverterResult(markdown);

        // Act
        var toString = result.ToString();

        // Assert
        Assert.Equal(markdown, toString);
    }

    [Fact]
    public void Constructor_WithSegments_SetsSegments()
    {
        // Arrange
        var segments = new List<DocumentSegment>
        {
            new DocumentSegment("Segment content", SegmentType.Page, number: 1, label: "Page 1"),
        };

        // Act
        var result = new DocumentConverterResult("# Test", "Title", segments);

        // Assert
        Assert.Single(result.Segments);
        Assert.Equal("Segment content", result.Segments[0].Markdown);
        Assert.Equal(SegmentType.Page, result.Segments[0].Type);
        Assert.Equal(1, result.Segments[0].Number);
    }

    [Fact]
    public void Constructor_WithoutSegments_ExposesEmptyCollection()
    {
        // Act
        var result = new DocumentConverterResult("# Test");

        // Assert
        Assert.NotNull(result.Segments);
        Assert.Empty(result.Segments);
    }

    [Fact]
    public void Markdown_IsEvaluatedPerAccess()
    {
        var callCount = 0;
        Func<string> factory = () =>
        {
            callCount++;
            return $"#{callCount}";
        };
        var result = DocumentConverterResult.FromFactory(
            markdownFactory: factory,
            title: null,
            segments: null,
            artifacts: null,
            metadata: null,
            artifactDirectory: null,
            cleanup: null,
            asyncCleanup: null,
            generatedAtUtc: null);

        Assert.Equal("#1", result.Markdown);
        Assert.Equal("#2", result.Markdown);
    }

    [Fact]
    public async Task DisposeAsync_InvokesCleanupHandle()
    {
        var cleanup = new TrackingCleanup();
        Func<string> factory = () => "ok";
        var result = DocumentConverterResult.FromFactory(
            markdownFactory: factory,
            title: null,
            segments: null,
            artifacts: null,
            metadata: null,
            artifactDirectory: null,
            cleanup: cleanup,
            asyncCleanup: cleanup,
            generatedAtUtc: null);

        await result.DisposeAsync();

        Assert.True(cleanup.AsyncDisposed);
        Assert.Equal(1, cleanup.DisposeCount);
    }

    private sealed class TrackingCleanup : IDisposable, IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public bool AsyncDisposed { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }

        public ValueTask DisposeAsync()
        {
            AsyncDisposed = true;
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}

public class ConverterRegistrationTests
{
    [Fact]
    public void Constructor_WithValidParameters_SetsProperties()
    {
        // Arrange
        var converter = new TestConverter();
        var priority = 5.0;

        // Act
        var registration = new ConverterRegistration(converter, priority);

        // Assert
        Assert.Equal(converter, registration.Converter);
        Assert.Equal(priority, registration.Priority);
    }

    [Fact]
    public void Constructor_WithNullConverter_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ConverterRegistration(null!, 1.0));
    }

    private class TestConverter : DocumentConverterBase
    {
        public TestConverter()
            : base(priority: 100)
        {
        }

        public override bool AcceptsInput(StreamInfo streamInfo)
        {
            return true;
        }

        public override bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        {
            return true;
        }

        public override Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DocumentConverterResult("Test"));
        }
    }
}
