using MarkItDown;
using System.Text;

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
    public void TextContent_ReturnsMarkdown()
    {
        // Arrange
        var markdown = "# Test\n\nThis is a test.";
        var result = new DocumentConverterResult(markdown);

        // Act
        var textContent = result.TextContent;

        // Assert
        Assert.Equal(markdown, textContent);
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

    private class TestConverter : IDocumentConverter
    {
        public int Priority => 100;

        public bool AcceptsInput(StreamInfo streamInfo)
        {
            return true;
        }

        public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        {
            return true;
        }

        public Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DocumentConverterResult("Test"));
        }
    }
}