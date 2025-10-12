using System;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;
using MarkItDown;
using MarkItDown.Converters;
using MarkItDown.Tests.Fixtures;
using Shouldly;

namespace MarkItDown.Tests;

public class DocxConverterTests
{
    [Fact]
    public async Task ConvertAsync_DocxWithImages_ExecutesPipelineAndCapturesArtifacts()
    {
        // Arrange
        var pipeline = new RecordingPipeline("DOCX ENRICHED");
        var converter = new DocxConverter(pipeline: pipeline);

        await using var stream = DocxInlineImageFactory.Create();
        var streamInfo = new StreamInfo(
            mimeType: MimeHelper.GetMimeType(".docx"),
            extension: ".docx",
            fileName: "doc-inline-image.docx");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        pipeline.Executed.ShouldBeTrue();
        result.Artifacts.Images.ShouldNotBeEmpty();
        result.Artifacts.TextBlocks.ShouldNotBeEmpty();

        var image = result.Artifacts.Images[0];
        image.SegmentIndex.ShouldNotBeNull();
        image.DetailedDescription.ShouldBe("DOCX ENRICHED");
        image.Metadata.ShouldContainKey(MetadataKeys.Page);
        image.PlaceholderMarkdown.ShouldNotBeNull();

        var segment = result.Segments[image.SegmentIndex!.Value];
        segment.Markdown.ShouldContain("DOCX ENRICHED");
        segment.Type.ShouldBe(SegmentType.Page);
        var placeholderIndex = segment.Markdown.IndexOf(image.PlaceholderMarkdown!, StringComparison.Ordinal);
        placeholderIndex.ShouldBeGreaterThanOrEqualTo(0);
        var trailing = segment.Markdown[(placeholderIndex + image.PlaceholderMarkdown!.Length)..];
        trailing.TrimStart('\r', '\n').ShouldStartWith("DOCX ENRICHED");
    }

    [Fact]
    public async Task ConvertAsync_ComplexDocx_PreservesRichContent()
    {
        var client = new MarkItDownClient();
        var path = TestAssetLoader.GetAssetPath(TestAssetCatalog.ComplexDocx);

        var result = await client.ConvertAsync(path);

        result.Title.ShouldBe("Rich Text Formatting");
        result.Markdown.ShouldContain("https://example.com/docs");
        result.Markdown.ShouldContain("| Metric | Q1 | Q2 | Total |");
        result.Markdown.ShouldContain("Equation: x^2 + y^2 = z^2");
        result.Markdown.ShouldContain("• Bullet list item one");
        result.Markdown.ShouldContain("![Image 1]");
    }

    [Fact]
    public async Task ConvertAsync_BrokenDocx_RaisesFileConversionError()
    {
        var client = new MarkItDownClient();
        var path = TestAssetLoader.GetAssetPath(TestAssetCatalog.BrokenDocx);

        var exception = await Should.ThrowAsync<UnsupportedFormatException>(async () => await client.ConvertAsync(path));
        exception.InnerException.ShouldNotBeNull();
        exception.InnerException.ShouldBeOfType<AggregateException>();
        var aggregate = (AggregateException)exception.InnerException!;
        aggregate.InnerExceptions.ShouldContain(e => e is FileConversionException);
    }
}
