using MarkItDown;
using MarkItDown.Converters;
using System.Text;

namespace MarkItDown.Tests;

public class PlainTextConverterTests
{
    [Fact]
    public void Accepts_TextFiles_ReturnsTrue()
    {
        // Arrange
        var converter = new PlainTextConverter();
        var streamInfo = new StreamInfo(extension: ".txt");
        using var stream = new MemoryStream();

        // Act
        var result = converter.Accepts(stream, streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Accepts_MarkdownFiles_ReturnsTrue()
    {
        // Arrange
        var converter = new PlainTextConverter();
        var streamInfo = new StreamInfo(extension: ".md");
        using var stream = new MemoryStream();

        // Act
        var result = converter.Accepts(stream, streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Accepts_JsonFiles_ReturnsFalse()
    {
        // Arrange
        var converter = new PlainTextConverter();
        var streamInfo = new StreamInfo(mimeType: "application/json");
        using var stream = new MemoryStream();

        // Act
        var result = converter.Accepts(stream, streamInfo);

        // Assert
        Assert.False(result); // JSON files should be handled by JsonConverter now
    }

    [Fact]
    public void Accepts_WithCharset_ReturnsTrue()
    {
        // Arrange
        var converter = new PlainTextConverter();
        var streamInfo = new StreamInfo(charset: Encoding.UTF8);
        using var stream = new MemoryStream();

        // Act
        var result = converter.Accepts(stream, streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Accepts_BinaryFiles_ReturnsFalse()
    {
        // Arrange
        var converter = new PlainTextConverter();
        var streamInfo = new StreamInfo(mimeType: "application/pdf", extension: ".pdf");
        using var stream = new MemoryStream();

        // Act
        var result = converter.Accepts(stream, streamInfo);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ConvertAsync_SimpleText_ReturnsCorrectMarkdown()
    {
        // Arrange
        var converter = new PlainTextConverter();
        var content = "Hello, World!";
        var bytes = Encoding.UTF8.GetBytes(content);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(charset: Encoding.UTF8);

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Equal(content, result.Markdown);
        Assert.Equal(content, result.ToString());
    }

    [Fact]
    public async Task ConvertAsync_EmptyStream_ReturnsEmptyString()
    {
        // Arrange
        var converter = new PlainTextConverter();
        using var stream = new MemoryStream();
        var streamInfo = new StreamInfo(charset: Encoding.UTF8);

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Equal(string.Empty, result.Markdown);
    }
}
