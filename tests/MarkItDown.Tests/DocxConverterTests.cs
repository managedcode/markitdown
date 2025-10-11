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
}
