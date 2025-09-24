using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;
using MarkItDown.Converters;
using Xunit;

namespace MarkItDown.Tests;

public class PdfConverterTests
{
    [Fact]
    public async Task ConvertAsync_CombinesTextAndImages()
    {
        // Arrange
        var textExtractor = new StubPdfTextExtractor("First page\n---\nSecond page");
        var imageRenderer = new StubPdfImageRenderer(new[] { "ZmFrZSBiYXNlNjQ=" });
        var converter = new PdfConverter(textExtractor, imageRenderer);

        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var streamInfo = new StreamInfo(mimeType: "application/pdf", extension: ".pdf", fileName: "sample.pdf");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("First page", result.Markdown);
        Assert.Contains("Second page", result.Markdown);
        Assert.Contains("## Page Images", result.Markdown);
        Assert.Contains("![PDF page 1](data:image/png;base64,ZmFrZSBiYXNlNjQ=)", result.Markdown);
    }

    [Fact]
    public void AcceptsInput_WithPdfExtension_ReturnsTrue()
    {
        var converter = new PdfConverter();
        var streamInfo = new StreamInfo(extension: ".pdf");

        Assert.True(converter.AcceptsInput(streamInfo));
    }

    [Fact]
    public void AcceptsInput_NonPdfExtension_ReturnsFalse()
    {
        var converter = new PdfConverter();
        var streamInfo = new StreamInfo(extension: ".txt", mimeType: "text/plain");

        Assert.False(converter.AcceptsInput(streamInfo));
    }

    private sealed class StubPdfTextExtractor : PdfConverter.IPdfTextExtractor
    {
        private readonly string text;

        public StubPdfTextExtractor(string text) => this.text = text;

        public Task<string> ExtractTextAsync(byte[] pdfBytes, CancellationToken cancellationToken)
        {
            return Task.FromResult(text);
        }
    }

    private sealed class StubPdfImageRenderer : PdfConverter.IPdfImageRenderer
    {
        private readonly IReadOnlyList<string> images;

        public StubPdfImageRenderer(IReadOnlyList<string> images) => this.images = images;

        public Task<IReadOnlyList<string>> RenderImagesAsync(byte[] pdfBytes, CancellationToken cancellationToken)
        {
            return Task.FromResult(images);
        }
    }
}
