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
}
