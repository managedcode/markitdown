using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;
using MarkItDown.Converters;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using Shouldly;
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
        Assert.Contains("![Image (page 1): PDF page 1](", result.Markdown);
        Assert.Equal(4, result.Segments.Count);
        Assert.Equal(SegmentType.Page, result.Segments[0].Type);
        Assert.Equal(1, result.Segments[0].Number);
        Assert.Contains("First page", result.Segments[0].Markdown);
        Assert.Equal(SegmentType.Page, result.Segments[1].Type);
        Assert.Equal(2, result.Segments[1].Number);
        Assert.Equal(SegmentType.Image, result.Segments[^1].Type);
        Assert.Equal(1, result.Segments[^1].Number);
        Assert.Single(result.Artifacts.Images);
        Assert.NotNull(result.Artifacts.Images[0].SegmentIndex);
    }

    [Fact]
    public async Task PdfConverter_PipelineEnrichesImages()
    {
        var pipeline = new RecordingPipeline("PIPELINE");
        var textExtractor = new StubPdfTextExtractor("Page body");
        var imageRenderer = new StubPdfImageRenderer(new[] { Convert.ToBase64String(new byte[] { 1, 2, 3 }) });
        var converter = new PdfConverter(textExtractor, imageRenderer, pipeline: pipeline);

        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var streamInfo = new StreamInfo(mimeType: "application/pdf", extension: ".pdf", fileName: "pipeline.pdf");

        var result = await converter.ConvertAsync(stream, streamInfo);

        Assert.True(pipeline.Executed);
        Assert.Single(result.Artifacts.Images);
        var image = result.Artifacts.Images[0];
        Assert.NotNull(image.SegmentIndex);
        Assert.Equal("PIPELINE", image.DetailedDescription);
        Assert.NotNull(image.PlaceholderMarkdown);
        Assert.StartsWith("![", image.PlaceholderMarkdown);
        Assert.Contains("PIPELINE", result.Segments[image.SegmentIndex!.Value].Markdown);
        var imageSegment = result.Segments[image.SegmentIndex!.Value];
        var placeholderIndex = imageSegment.Markdown.IndexOf(image.PlaceholderMarkdown!, StringComparison.Ordinal);
        Assert.True(placeholderIndex >= 0);
        var trailing = imageSegment.Markdown[(placeholderIndex + image.PlaceholderMarkdown!.Length)..];
        Assert.StartsWith("PIPELINE", trailing.TrimStart('\r', '\n'));
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

    [Fact]
    public async Task ConvertAsync_TreatPagesAsImages_UsesOcrFromImageProvider()
    {
        var pageImage = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 });
        var textExtractor = new StubPdfTextExtractor();
        var imageRenderer = new StubPdfImageRenderer(new[] { pageImage });

        var segmentOptions = new SegmentOptions
        {
            Pdf = new PdfSegmentOptions { TreatPagesAsImages = true },
            Image = SegmentOptions.Default.Image with { EnableImageUnderstandingProvider = true }
        };

        var imageProvider = new StubImageUnderstandingProvider("Rendered snapshot", "Recognized OCR line.");
        var converter = new PdfConverter(textExtractor, imageRenderer, segmentOptions, imageProvider: imageProvider);

        await using var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        var streamInfo = new StreamInfo(mimeType: "application/pdf", extension: ".pdf", fileName: "ocr.pdf");

        var result = await converter.ConvertAsync(stream, streamInfo);

        Assert.Single(result.Segments);
        var segment = result.Segments[0];
        Assert.Equal(SegmentType.Page, segment.Type);
        Assert.Contains("Recognized OCR line.", segment.Markdown);
        Assert.Contains("![", segment.Markdown);

        Assert.Single(result.Artifacts.Images);
        var artifact = result.Artifacts.Images[0];
        Assert.Equal(segment.Number, artifact.PageNumber);
        Assert.NotNull(artifact.PlaceholderMarkdown);
        Assert.Contains("Recognized OCR line.", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_DocumentIntMergedCells_DuplicatesValuesAcrossRows()
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "ITEM", "CONDITION", "QUANTITY", "LOCATION" },
            new[] { "Laptop", "Refurbished", "5", "Aisle 3" },
            new[] { "Laptop", "Like-New (Open Box)", "3", "Aisle 3" },
            new[] { "Laptop", "Used (Grade A)", "4", "Aisle 5" }
        };

        var table = new DocumentTableResult(1, rows);
        var page = new DocumentPageResult(1, string.Empty, new[] { 0 });
        var providerResult = new DocumentIntelligenceResult(new[] { page }, new[] { table }, Array.Empty<DocumentImageResult>());
        var provider = new StubDocumentIntelligenceProvider(providerResult);
        var converter = new PdfConverter(documentProvider: provider);

        await using var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        var streamInfo = new StreamInfo(mimeType: "application/pdf", extension: ".pdf", fileName: "merged.pdf");

        var result = await converter.ConvertAsync(stream, streamInfo);

        result.Markdown.ShouldContain("| Laptop | Refurbished | 5 | Aisle 3 |");
        result.Markdown.ShouldContain("| Laptop | Like-New (Open Box) | 3 | Aisle 3 |");
        result.Markdown.ShouldContain("| Laptop | Used (Grade A) | 4 | Aisle 5 |");
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

        public Task<IReadOnlyList<PdfConverter.PdfPageText>> ExtractTextAsync(string pdfPath, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PdfConverter.PdfPageText>>(pages);
    }

    private sealed class StubPdfImageRenderer : PdfConverter.IPdfImageRenderer
    {
        private readonly IReadOnlyList<string> images;

        public StubPdfImageRenderer(IReadOnlyList<string> images) => this.images = images;

        public Task<IReadOnlyList<string>> RenderImagesAsync(string pdfPath, CancellationToken cancellationToken)
        {
            return Task.FromResult(images);
        }
    }

    private sealed class StubImageUnderstandingProvider : IImageUnderstandingProvider
    {
        private readonly ImageUnderstandingResult result;

        public StubImageUnderstandingProvider(string caption, string text)
        {
            result = new ImageUnderstandingResult(
                caption,
                text,
                Array.Empty<string>(),
                Array.Empty<string>(),
                new Dictionary<string, string>());
        }

        public Task<ImageUnderstandingResult?> AnalyzeAsync(Stream stream, StreamInfo streamInfo, ImageUnderstandingRequest? request = null, CancellationToken cancellationToken = default)
            => Task.FromResult<ImageUnderstandingResult?>(result);
    }

    private sealed class StubDocumentIntelligenceProvider : IDocumentIntelligenceProvider
    {
        private readonly DocumentIntelligenceResult? result;

        public StubDocumentIntelligenceProvider(DocumentIntelligenceResult? result)
        {
            this.result = result;
        }

        public Task<DocumentIntelligenceResult?> AnalyzeAsync(Stream stream, StreamInfo streamInfo, DocumentIntelligenceRequest? request = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }
}
