using System;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;
using MarkItDown;
using MarkItDown.Converters;
using MarkItDown.Tests.Fixtures;
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

        await using var stream = TestAssetLoader.OpenAsset(TestAssetCatalog.AutogenStrategyPptx);
        var streamInfo = new StreamInfo(
            mimeType: MimeHelper.GetMimeType(".pptx"),
            extension: ".pptx",
            fileName: "autogen-strategy.pptx");

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

    [Fact]
    public async Task ConvertAsync_ComplexPptx_EmitsSlidesAndArtifacts()
    {
        var client = new MarkItDownClient();
        var path = TestAssetLoader.GetAssetPath(TestAssetCatalog.ComplexPptx);

        var result = await client.ConvertAsync(path);

        result.Title.ShouldBe("Slide 1");
        result.Markdown.ShouldContain("## Slide 2");
        result.Markdown.ShouldContain("Quarterly Breakdown");
        result.Markdown.ShouldContain("Key wins: automation, coverage, resilience");
        result.Markdown.ShouldContain("![ComplexFixtureImage]");
    }

    [Fact]
    public async Task ConvertAsync_BrokenPptx_RaisesFileConversionError()
    {
        var client = new MarkItDownClient();
        var path = TestAssetLoader.GetAssetPath(TestAssetCatalog.BrokenPptx);

        var exception = await Should.ThrowAsync<UnsupportedFormatException>(async () => await client.ConvertAsync(path));
        exception.InnerException.ShouldNotBeNull();
        exception.InnerException.ShouldBeOfType<AggregateException>();
        var aggregate = (AggregateException)exception.InnerException!;
        aggregate.InnerExceptions.ShouldContain(e => e is FileConversionException);
    }
}
