using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        var textExtractor = new StubPdfTextExtractor("First page", "Second page");
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
        Assert.Equal(4, result.Segments.Count);
        Assert.Equal(SegmentType.Page, result.Segments[0].Type);
        Assert.Equal(1, result.Segments[0].Number);
        Assert.Contains("First page", result.Segments[0].Markdown);
        Assert.Equal(SegmentType.Page, result.Segments[1].Type);
        Assert.Equal(2, result.Segments[1].Number);
        Assert.Equal(SegmentType.Image, result.Segments[^1].Type);
        Assert.Equal(1, result.Segments[^1].Number);
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
        private readonly IReadOnlyList<PdfConverter.PdfPageText> pages;

        public StubPdfTextExtractor(params string[] pages)
        {
            this.pages = pages
                .Select((text, index) => new PdfConverter.PdfPageText(index + 1, text))
                .ToList();
        }

        public Task<IReadOnlyList<PdfConverter.PdfPageText>> ExtractTextAsync(byte[] pdfBytes, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PdfConverter.PdfPageText>>(pages);
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
