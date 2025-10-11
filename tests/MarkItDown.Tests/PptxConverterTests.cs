using System;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;
using MarkItDown;
using MarkItDown.Converters;
using Shouldly;

namespace MarkItDown.Tests;

public class PptxConverterTests
{
    [Fact]
    public async Task ConvertAsync_PptxWithImages_ExecutesPipelineAndCapturesArtifacts()
    {
        // Arrange
        var pipeline = new RecordingPipeline("PPTX ENRICHED");
        var converter = new PptxConverter(pipeline: pipeline);

        await using var stream = TestAssetLoader.OpenAsset("test.pptx");
        var streamInfo = new StreamInfo(
            mimeType: MimeHelper.GetMimeType(".pptx"),
            extension: ".pptx",
            fileName: "test.pptx");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        pipeline.Executed.ShouldBeTrue();
        result.Artifacts.Images.ShouldNotBeEmpty();
        result.Artifacts.TextBlocks.ShouldNotBeEmpty();

        var image = result.Artifacts.Images[0];
        image.SegmentIndex.ShouldNotBeNull();
        image.DetailedDescription.ShouldBe("PPTX ENRICHED");
        image.Metadata.ShouldContainKey(MetadataKeys.Slide);
        image.PlaceholderMarkdown.ShouldNotBeNull();

        var segment = result.Segments[image.SegmentIndex!.Value];
        segment.Markdown.ShouldContain("PPTX ENRICHED");
        segment.Type.ShouldBe(SegmentType.Slide);
        var placeholderIndex = segment.Markdown.IndexOf(image.PlaceholderMarkdown!, StringComparison.Ordinal);
        placeholderIndex.ShouldBeGreaterThanOrEqualTo(0);
        var trailing = segment.Markdown[(placeholderIndex + image.PlaceholderMarkdown!.Length)..];
        trailing.TrimStart('\r', '\n').ShouldStartWith("PPTX ENRICHED");
    }
}
