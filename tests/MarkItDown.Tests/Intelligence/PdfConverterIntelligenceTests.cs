using System.Linq;
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

        public Task<DocumentIntelligenceResult?> AnalyzeAsync(Stream stream, StreamInfo streamInfo, DocumentIntelligenceRequest? request = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class StubPdfTextExtractor : PdfConverter.IPdfTextExtractor
    {
        public Task<IReadOnlyList<PdfConverter.PdfPageText>> ExtractTextAsync(string pdfPath, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PdfConverter.PdfPageText>>(Array.Empty<PdfConverter.PdfPageText>());
    }

    private sealed class StubPdfImageRenderer : PdfConverter.IPdfImageRenderer
    {
        private readonly IReadOnlyList<string> images;

        public StubPdfImageRenderer(IReadOnlyList<string> images)
        {
            this.images = images;
        }

        public Task<IReadOnlyList<string>> RenderImagesAsync(string pdfPath, CancellationToken cancellationToken)
            => Task.FromResult(images);
    }
    private static readonly int[] tableIndices = new[] { 0 };
    private static readonly string[] item = new[] { "Name", "Value" };
    private static readonly string[] itemArray = new[] { "Alpha", "42" };

    [Fact]
    public async Task ConvertAsync_WithDocumentIntelligenceProvider_UsesProviderSegments()
    {
        var page = new DocumentPageResult(
            pageNumber: 1,
            text: "**Summary**\n\nThis is page one.",
            tableIndices: tableIndices);

        var table = new DocumentTableResult(
            pageNumber: 1,
            rows: new List<IReadOnlyList<string>>
            { item,
                itemArray
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
        var segmentOptions = new SegmentOptions
        {
            Pdf = new PdfSegmentOptions
            {
                TreatPagesAsImages = false
            }
        };

        var converter = new PdfConverter(segmentOptions: segmentOptions, documentProvider: provider);

        await using var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // %PDF
        var streamInfo = new StreamInfo(mimeType: "application/pdf", extension: ".pdf", fileName: "sample.pdf");

        var result = await converter.ConvertAsync(stream, streamInfo);

        result.Segments.Count.ShouldBe(1);
        var pageSegment = result.Segments[0];
        pageSegment.Type.ShouldBe(SegmentType.Page);
        pageSegment.Markdown.ShouldContain("This is page one");
        pageSegment.Markdown.ShouldContain("Alpha");
        pageSegment.Markdown.ShouldContain("Chart overview");
        result.Markdown.ShouldContain("Summary");
        result.Artifacts.Tables.ShouldHaveSingleItem().Rows.ShouldContain(row => row.Contains("Alpha"));
        result.Artifacts.Images.ShouldHaveSingleItem().Label.ShouldBe("Chart overview");
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
        var segmentOptions = new SegmentOptions
        {
            Pdf = new PdfSegmentOptions
            {
                TreatPagesAsImages = true
            }
        };

        var converter = new PdfConverter(textExtractor, imageRenderer, segmentOptions, provider, pipeline: pipeline);

        await using var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        var streamInfo = new StreamInfo(mimeType: "application/pdf", extension: ".pdf", fileName: "snapshot.pdf");

        var result = await converter.ConvertAsync(stream, streamInfo);

        pipeline.Executed.ShouldBeTrue();
        result.Artifacts.Images.ShouldNotBeEmpty();

        var snapshot = result.Artifacts.Images[0];
        snapshot.SegmentIndex.ShouldNotBeNull();
        snapshot.DetailedDescription.ShouldBe("SNAPSHOT ENRICHED");
        snapshot.Metadata.ShouldContainKey(MetadataKeys.Snapshot);

        var segment = result.Segments[snapshot.SegmentIndex!.Value];
        segment.Type.ShouldBe(SegmentType.Page);
        segment.Markdown.ShouldContain("SNAPSHOT ENRICHED");
        snapshot.PlaceholderMarkdown.ShouldNotBeNull();
        segment.Markdown.ShouldContain(snapshot.PlaceholderMarkdown!);
    }
}
