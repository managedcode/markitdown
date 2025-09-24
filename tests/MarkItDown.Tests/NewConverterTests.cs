using System.Text;
using MarkItDown;
using MarkItDown.Converters;
using Xunit;

namespace MarkItDown.Tests;

public class NewConverterTests
{
    [Fact]
    public void CsvConverter_AcceptsInput_ValidCsvExtension_ReturnsTrue()
    {
        // Arrange
        var converter = new CsvConverter();
        var streamInfo = new StreamInfo(extension: ".csv");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CsvConverter_AcceptsInput_CsvMimeType_ReturnsTrue()
    {
        // Arrange
        var converter = new CsvConverter();
        var streamInfo = new StreamInfo(mimeType: "text/csv");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CsvConverter_ConvertAsync_ValidCsv_ReturnsMarkdownTable()
    {
        // Arrange
        var converter = new CsvConverter();
        var csvContent = "Name,Age,City\nJohn,25,New York\nJane,30,Boston";
        var bytes = Encoding.UTF8.GetBytes(csvContent);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(mimeType: "text/csv");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("| Name | Age | City |", result.Markdown);
        Assert.Contains("| --- | --- | --- |", result.Markdown);
        Assert.Contains("| John | 25 | New York |", result.Markdown);
        Assert.Contains("| Jane | 30 | Boston |", result.Markdown);
    }

    [Fact]
    public void JsonConverter_AcceptsInput_ValidJsonExtension_ReturnsTrue()
    {
        // Arrange
        var converter = new JsonConverter();
        var streamInfo = new StreamInfo(extension: ".json");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void JsonConverter_AcceptsInput_JsonMimeType_ReturnsTrue()
    {
        // Arrange
        var converter = new JsonConverter();
        var streamInfo = new StreamInfo(mimeType: "application/json");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task JsonConverter_ConvertAsync_ValidJson_ReturnsMarkdown()
    {
        // Arrange
        var converter = new JsonConverter();
        var jsonContent = "{\"name\": \"John\", \"age\": 25, \"city\": \"New York\"}";
        var bytes = Encoding.UTF8.GetBytes(jsonContent);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(mimeType: "application/json", fileName: "test.json");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("# test", result.Markdown);
        Assert.Contains("```json", result.Markdown);
        Assert.Contains("\"name\": \"John\"", result.Markdown);
        Assert.Contains("\"age\": 25", result.Markdown);
        Assert.Contains("\"city\": \"New York\"", result.Markdown);
        Assert.EndsWith("```", result.Markdown.TrimEnd());
    }

    [Fact]
    public void XmlConverter_AcceptsInput_ValidXmlExtension_ReturnsTrue()
    {
        // Arrange
        var converter = new XmlConverter();
        var streamInfo = new StreamInfo(extension: ".xml");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void XmlConverter_AcceptsInput_XmlMimeType_ReturnsTrue()
    {
        // Arrange
        var converter = new XmlConverter();
        var streamInfo = new StreamInfo(mimeType: "application/xml");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task XmlConverter_ConvertAsync_ValidXml_ReturnsMarkdown()
    {
        // Arrange
        var converter = new XmlConverter();
        var xmlContent = "<?xml version=\"1.0\"?><root><title>Test Document</title><content>Sample content</content></root>";
        var bytes = Encoding.UTF8.GetBytes(xmlContent);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(mimeType: "application/xml");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("## Root", result.Markdown);
        Assert.Contains("### Title", result.Markdown);
        Assert.Contains("Test Document", result.Markdown);
        Assert.Contains("### Content", result.Markdown);
        Assert.Contains("Sample content", result.Markdown);
    }

    [Fact]
    public void ZipConverter_AcceptsInput_ValidZipExtension_ReturnsTrue()
    {
        // Arrange
        var converter = new ZipConverter();
        var streamInfo = new StreamInfo(extension: ".zip");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ZipConverter_AcceptsInput_ZipMimeType_ReturnsTrue()
    {
        // Arrange
        var converter = new ZipConverter();
        var streamInfo = new StreamInfo(mimeType: "application/zip");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EpubConverter_AcceptsInput_ValidEpubExtension_ReturnsTrue()
    {
        // Arrange
        var converter = new EpubConverter();
        var streamInfo = new StreamInfo(extension: ".epub");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EpubConverter_AcceptsInput_EpubMimeType_ReturnsTrue()
    {
        // Arrange
        var converter = new EpubConverter();
        var streamInfo = new StreamInfo(mimeType: "application/epub+zip");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void AllNewConverters_HaveValidPriorities()
    {
        // Arrange
        var converters = new IDocumentConverter[]
        {
            new CsvConverter(),
            new JsonConverter(),
            new JupyterNotebookConverter(),
            new XmlConverter(),
            new RssFeedConverter(),
            new ZipConverter(),
            new EpubConverter(),
            new YouTubeUrlConverter()
        };

        // Act & Assert
        foreach (var converter in converters)
        {
            Assert.True(converter.Priority > 0, $"{converter.GetType().Name} should have positive priority");
            Assert.True(converter.Priority < 1000, $"{converter.GetType().Name} should have priority less than PlainTextConverter");
        }
    }

    [Theory]
    [InlineData(".csv", "text/csv")]
    [InlineData(".json", "application/json")]
    [InlineData(".ipynb", "application/x-ipynb+json")]
    [InlineData(".xml", "application/xml")]
    [InlineData(".rss", "application/rss+xml")]
    [InlineData(".zip", "application/zip")]
    [InlineData(".epub", "application/epub+zip")]
    public void MarkItDown_RegistersNewConverters_CanHandleNewFormats(string extension, string mimeType)
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDown();
        var converters = markItDown.GetRegisteredConverters();

        // Act
        var streamInfo = new StreamInfo(mimeType: mimeType, extension: extension);
        var hasConverter = converters.Any(c => c.AcceptsInput(streamInfo));

        // Assert
        Assert.True(hasConverter, $"No converter found for {extension} files with MIME type {mimeType}");
    }

    [Fact]
    public void JupyterNotebookConverter_AcceptsInput_ValidIpynbExtension_ReturnsTrue()
    {
        // Arrange
        var converter = new JupyterNotebookConverter();
        var streamInfo = new StreamInfo(extension: ".ipynb");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void RssFeedConverter_AcceptsInput_ValidRssExtension_ReturnsTrue()
    {
        // Arrange
        var converter = new RssFeedConverter();
        var streamInfo = new StreamInfo(extension: ".rss");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void RssFeedConverter_AcceptsInput_AtomMimeType_ReturnsTrue()
    {
        // Arrange
        var converter = new RssFeedConverter();
        var streamInfo = new StreamInfo(mimeType: "application/atom+xml");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void YouTubeUrlConverter_AcceptsInput_ValidYouTubeUrl_ReturnsTrue()
    {
        // Arrange
        var converter = new YouTubeUrlConverter();
        var streamInfo = new StreamInfo(url: "https://www.youtube.com/watch?v=dQw4w9WgXcQ");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void YouTubeUrlConverter_AcceptsInput_ShortenedYouTubeUrl_ReturnsTrue()
    {
        // Arrange
        var converter = new YouTubeUrlConverter();
        var streamInfo = new StreamInfo(url: "https://youtu.be/dQw4w9WgXcQ");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void YouTubeUrlConverter_AcceptsInput_NonYouTubeUrl_ReturnsFalse()
    {
        // Arrange
        var converter = new YouTubeUrlConverter();
        var streamInfo = new StreamInfo(url: "https://www.example.com/video");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task JupyterNotebookConverter_ConvertAsync_ValidNotebook_ReturnsMarkdown()
    {
        // Arrange
        var converter = new JupyterNotebookConverter();
        var notebookContent = """
        {
          "nbformat": 4,
          "nbformat_minor": 2,
          "metadata": {
            "kernelspec": {
              "display_name": "Python 3",
              "language": "python"
            }
          },
          "cells": [
            {
              "cell_type": "markdown",
              "source": ["# Hello World\n", "This is a markdown cell."]
            },
            {
              "cell_type": "code",
              "source": ["print('Hello, World!')"],
              "outputs": [
                {
                  "output_type": "stream",
                  "text": ["Hello, World!\n"]
                }
              ]
            }
          ]
        }
        """;
        var bytes = Encoding.UTF8.GetBytes(notebookContent);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(mimeType: "application/x-ipynb+json", fileName: "test.ipynb");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("# test", result.Markdown);
        Assert.Contains("**Kernel:** Python 3", result.Markdown);
        Assert.Contains("# Hello World", result.Markdown);
        Assert.Contains("This is a markdown cell.", result.Markdown);
        Assert.Contains("## Code Cell", result.Markdown);
        Assert.Contains("print('Hello, World!')", result.Markdown);
        Assert.Contains("**Output:**", result.Markdown);
    }

    [Fact]
    public async Task RssFeedConverter_ConvertAsync_ValidRssFeed_ReturnsMarkdown()
    {
        // Arrange
        var converter = new RssFeedConverter();
        var rssContent = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
          <channel>
            <title>Example RSS Feed</title>
            <description>A sample RSS feed for testing</description>
            <link>https://example.com</link>
            <item>
              <title>First Post</title>
              <description>This is the first post</description>
              <link>https://example.com/post1</link>
              <pubDate>Mon, 01 Jan 2024 12:00:00 GMT</pubDate>
            </item>
          </channel>
        </rss>
        """;
        var bytes = Encoding.UTF8.GetBytes(rssContent);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(mimeType: "application/rss+xml");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("# Example RSS Feed", result.Markdown);
        Assert.Contains("**Description:** A sample RSS feed for testing", result.Markdown);
        Assert.Contains("## Items", result.Markdown);
        Assert.Contains("### [First Post](https://example.com/post1)", result.Markdown);
        Assert.Contains("This is the first post", result.Markdown);
    }
}
