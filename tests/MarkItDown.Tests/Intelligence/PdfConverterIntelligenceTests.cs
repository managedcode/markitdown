using MarkItDown;
using MarkItDown.Converters;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using Shouldly;

namespace MarkItDown.Tests.Intelligence;

public class PdfConverterIntelligenceTests
{
    private sealed class StubDocumentIntelligenceProvider : IDocumentIntelligenceProvider
    {
        private readonly DocumentIntelligenceResult? result;

        public StubDocumentIntelligenceProvider(DocumentIntelligenceResult? result)
        {
            this.result = result;
        }

        public Task<DocumentIntelligenceResult?> AnalyzeAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class StubPdfTextExtractor : PdfConverter.IPdfTextExtractor
    {
        public Task<IReadOnlyList<PdfConverter.PdfPageText>> ExtractTextAsync(byte[] pdfBytes, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PdfConverter.PdfPageText>>(Array.Empty<PdfConverter.PdfPageText>());
    }

    private sealed class StubPdfImageRenderer : PdfConverter.IPdfImageRenderer
    {
        private readonly IReadOnlyList<string> images;

        public StubPdfImageRenderer(IReadOnlyList<string> images)
        {
            this.images = images;
        }

        public Task<IReadOnlyList<string>> RenderImagesAsync(byte[] pdfBytes, CancellationToken cancellationToken)
            => Task.FromResult(images);
    }

    [Fact]
    public async Task ConvertAsync_WithDocumentIntelligenceProvider_UsesProviderSegments()
    {
        var page = new DocumentPageResult(
            pageNumber: 1,
            text: "**Summary**\n\nThis is page one.",
            tableIndices: new[] { 0 });

        var table = new DocumentTableResult(
            pageNumber: 1,
            rows: new List<IReadOnlyList<string>>
            {
                new[] { "Name", "Value" },
                new[] { "Alpha", "42" }
            });

        var image = new DocumentImageResult(
            pageNumber: 1,
            content: new byte[] { 1, 2, 3 },
            contentType: "image/png",
            caption: "Chart overview");

        var providerResult = new DocumentIntelligenceResult(
            pages: new[] { page },
            tables: new[] { table },
            images: new[] { image });

        var provider = new StubDocumentIntelligenceProvider(providerResult);
        var converter = new PdfConverter(documentProvider: provider);

        await using var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // %PDF
        var streamInfo = new StreamInfo(mimeType: "application/pdf", extension: ".pdf", fileName: "sample.pdf");

        var result = await converter.ConvertAsync(stream, streamInfo);

        result.Segments.Count.ShouldBeGreaterThanOrEqualTo(2);
        result.Segments[0].Type.ShouldBe(SegmentType.Page);
        result.Segments[0].Markdown.ShouldContain("This is page one");
        result.Segments.ShouldContain(s => s.Type == SegmentType.Table && s.Markdown.Contains("Alpha"));
        result.Segments.ShouldContain(s => s.Type == SegmentType.Image && s.Markdown.Contains("Chart overview"));
        result.Markdown.ShouldContain("Summary");
    }

    [Fact]
    public async Task ConvertAsync_DocumentIntelligenceWithoutImages_AddsPageSnapshotsForPipeline()
    {
        var page = new DocumentPageResult(
            pageNumber: 1,
            text: "Page one body",
            tableIndices: Array.Empty<int>());

        var providerResult = new DocumentIntelligenceResult(
            pages: new[] { page },
            tables: Array.Empty<DocumentTableResult>(),
            images: Array.Empty<DocumentImageResult>());

        var provider = new StubDocumentIntelligenceProvider(providerResult);
        var pipeline = new RecordingPipeline("SNAPSHOT ENRICHED");
        var textExtractor = new StubPdfTextExtractor();
        var pageImage = Convert.ToBase64String(new byte[] { 5, 4, 3, 2 });
        var imageRenderer = new StubPdfImageRenderer(new[] { pageImage });
        var converter = new PdfConverter(textExtractor, imageRenderer, pipeline: pipeline, documentProvider: provider);

        await using var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        var streamInfo = new StreamInfo(mimeType: "application/pdf", extension: ".pdf", fileName: "snapshot.pdf");

        var result = await converter.ConvertAsync(stream, streamInfo);

        pipeline.Executed.ShouldBeTrue();
        result.Artifacts.Images.ShouldNotBeEmpty();

        var snapshot = result.Artifacts.Images[0];
        snapshot.SegmentIndex.ShouldNotBeNull();
        snapshot.DetailedDescription.ShouldBe("SNAPSHOT ENRICHED");
        snapshot.Metadata.ShouldContainKey("snapshot");

        var segment = result.Segments[snapshot.SegmentIndex!.Value];
        segment.Type.ShouldBe(SegmentType.Image);
        segment.AdditionalMetadata.ShouldContainKey("snapshot");
        segment.Markdown.ShouldContain("SNAPSHOT ENRICHED");
        result.Segments.ShouldContain(s => s.Type == SegmentType.Section && s.Label == "Page Snapshots");
    }
}
