using MarkItDown;
using MarkItDown.Converters;
using System.Text;

namespace MarkItDown.Tests;

public class HtmlConverterTests
{
    [Fact]
    public void Accepts_HtmlFiles_ReturnsTrue()
    {
        // Arrange
        var converter = new HtmlConverter();
        var streamInfo = new StreamInfo(extension: ".html");
        using var stream = new MemoryStream();

        // Act
        var result = converter.Accepts(stream, streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Accepts_HtmFiles_ReturnsTrue()
    {
        // Arrange
        var converter = new HtmlConverter();
        var streamInfo = new StreamInfo(extension: ".htm");
        using var stream = new MemoryStream();

        // Act
        var result = converter.Accepts(stream, streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Accepts_HtmlMimeType_ReturnsTrue()
    {
        // Arrange
        var converter = new HtmlConverter();
        var streamInfo = new StreamInfo(mimeType: "text/html");
        using var stream = new MemoryStream();

        // Act
        var result = converter.Accepts(stream, streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Accepts_XhtmlMimeType_ReturnsTrue()
    {
        // Arrange
        var converter = new HtmlConverter();
        var streamInfo = new StreamInfo(mimeType: "application/xhtml+xml");
        using var stream = new MemoryStream();

        // Act
        var result = converter.Accepts(stream, streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Accepts_NonHtmlFiles_ReturnsFalse()
    {
        // Arrange
        var converter = new HtmlConverter();
        var streamInfo = new StreamInfo(mimeType: "application/pdf", extension: ".pdf");
        using var stream = new MemoryStream();

        // Act
        var result = converter.Accepts(stream, streamInfo);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ConvertAsync_SimpleHtml_ReturnsCorrectMarkdown()
    {
        // Arrange
        var converter = new HtmlConverter();
        var html = "<html><head><title>Test Page</title></head><body><h1>Hello</h1><p>This is a <strong>test</strong>.</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(charset: Encoding.UTF8);

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("# Hello", result.Markdown);
        Assert.Contains("This is a **test**.", result.Markdown);
        Assert.Equal("Test Page", result.Title);
    }

    [Fact]
    public async Task ConvertAsync_HtmlWithLink_CreatesMarkdownLink()
    {
        // Arrange
        var converter = new HtmlConverter();
        var html = "<html><body><a href=\"https://example.com\">Example Link</a></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(charset: Encoding.UTF8);

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("[Example Link](https://example.com)", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_HtmlWithImage_CreatesMarkdownImage()
    {
        // Arrange
        var converter = new HtmlConverter();
        var html = "<html><body><img src=\"image.jpg\" alt=\"Test Image\" /></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(charset: Encoding.UTF8);

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("![Test Image](image.jpg)", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_HtmlWithList_CreatesMarkdownList()
    {
        // Arrange
        var converter = new HtmlConverter();
        var html = "<html><body><ul><li>Item 1</li><li>Item 2</li></ul></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(charset: Encoding.UTF8);

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("- Item 1", result.Markdown);
        Assert.Contains("- Item 2", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_HtmlWithTable_CreatesMarkdownTable()
    {
        // Arrange
        var converter = new HtmlConverter();
        var html = "<html><body><table><tr><th>Header 1</th><th>Header 2</th></tr><tr><td>Cell 1</td><td>Cell 2</td></tr></table></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(charset: Encoding.UTF8);

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("| Header 1 | Header 2 |", result.Markdown);
        Assert.Contains("| --- | --- |", result.Markdown);
        Assert.Contains("| Cell 1 | Cell 2 |", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_HtmlWithBlockquote_CreatesMarkdownBlockquote()
    {
        // Arrange
        var converter = new HtmlConverter();
        var html = "<html><body><blockquote>This is a quote</blockquote></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(charset: Encoding.UTF8);

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("> This is a quote", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_HtmlWithCode_CreatesMarkdownCode()
    {
        // Arrange
        var converter = new HtmlConverter();
        var html = "<html><body><p>This is <code>inline code</code>.</p><pre>This is a code block</pre></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(charset: Encoding.UTF8);

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("`inline code`", result.Markdown);
        Assert.Contains("```", result.Markdown);
        Assert.Contains("This is a code block", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_EmptyHtml_ReturnsEmptyString()
    {
        // Arrange
        var converter = new HtmlConverter();
        using var stream = new MemoryStream();
        var streamInfo = new StreamInfo(charset: Encoding.UTF8);

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Equal(string.Empty, result.Markdown);
    }
}