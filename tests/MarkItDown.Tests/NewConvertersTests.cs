using System.IO;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;
using MarkItDown;
using MarkItDown.Converters;
using MarkItDown.Tests.Fixtures;
using Shouldly;

namespace MarkItDown.Tests;

public class NewConvertersTests
{
    [Fact]
    public void PdfConverter_AcceptsInput_ValidPdfExtension_ReturnsTrue()
    {
        // Arrange
        var converter = new PdfConverter();
        var streamInfo = new StreamInfo(mimeType: "application/pdf", extension: ".pdf");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void PdfConverter_AcceptsInput_InvalidExtension_ReturnsFalse()
    {
        // Arrange
        var converter = new PdfConverter();
        var streamInfo = new StreamInfo(mimeType: "text/plain", extension: ".txt");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void DocxConverter_AcceptsInput_ValidDocxExtension_ReturnsTrue()
    {
        // Arrange
        var converter = new DocxConverter();
        var streamInfo = new StreamInfo(
            mimeType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 
            extension: ".docx");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void XlsxConverter_AcceptsInput_ValidXlsxExtension_ReturnsTrue()
    {
        // Arrange
        var converter = new XlsxConverter();
        var streamInfo = new StreamInfo(
            mimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 
            extension: ".xlsx");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void PptxConverter_AcceptsInput_ValidPptxExtension_ReturnsTrue()
    {
        // Arrange
        var converter = new PptxConverter();
        var streamInfo = new StreamInfo(
            mimeType: "application/vnd.openxmlformats-officedocument.presentationml.presentation", 
            extension: ".pptx");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void ImageOcrConverter_AcceptsInput_ValidImageExtension_ReturnsTrue()
    {
        // Arrange
        var converter = new ImageConverter();
        var streamInfo = new StreamInfo(mimeType: "image/jpeg", extension: ".jpg");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void ImageOcrConverter_AcceptsInput_MultipleImageFormats_ReturnsTrue()
    {
        // Arrange
        var converter = new ImageConverter();
        var testCases = new[]
        {
            (".png", "image/png"),
            (".gif", "image/gif"),
            (".bmp", "image/bmp"),
            (".tiff", "image/tiff"),
            (".webp", "image/webp")
        };

        foreach (var (extension, mimeType) in testCases)
        {
            var streamInfo = new StreamInfo(mimeType: mimeType, extension: extension);

            // Act
            var result = converter.AcceptsInput(streamInfo);

            // Assert
            result.ShouldBeTrue($"Should accept {extension} files");
        }
    }

    [Fact]
    public void AllNewConverters_HaveCorrectPriorities()
    {
        // Arrange & Act
        var pdfConverter = new PdfConverter();
        var docxConverter = new DocxConverter();
        var xlsxConverter = new XlsxConverter();
        var pptxConverter = new PptxConverter();
        var imageConverter = new ImageConverter();

        // Assert - Ensure proper priority ordering
        pdfConverter.Priority.ShouldBe(200);
        docxConverter.Priority.ShouldBe(210);
        xlsxConverter.Priority.ShouldBe(220);
        pptxConverter.Priority.ShouldBe(230);
        imageConverter.Priority.ShouldBe(450); // Lower priority due to resource intensity
    }

    [Theory]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData(".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".eml", "message/rfc822")]
    public void MarkItDown_RegistersNewConverters_CanHandleNewFormats(string extension, string mimeType)
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDownClient();
        var registeredConverters = markItDown.GetRegisteredConverters();

        // Act
        var streamInfo = new StreamInfo(mimeType: mimeType, extension: extension);
        var canHandle = registeredConverters.Any(c => c.AcceptsInput(streamInfo));

        // Assert
        canHandle.ShouldBeTrue($"Should have a converter that can handle {extension} files");
    }

    [Fact]
    public void EmlConverter_AcceptsInput_ValidEmlExtension_ReturnsTrue()
    {
        // Arrange
        var converter = new EmlConverter();
        var streamInfo = new StreamInfo(mimeType: "message/rfc822", extension: ".eml");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void EmlConverter_AcceptsInput_InvalidExtension_ReturnsFalse()
    {
        // Arrange
        var converter = new EmlConverter();
        var streamInfo = new StreamInfo(mimeType: "text/plain", extension: ".txt");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData(".eml", "message/rfc822")]
    [InlineData(".eml", "message/email")]
    [InlineData(".eml", "application/email")]
    [InlineData(".eml", "text/email")]
    public void EmlConverter_AcceptsInput_ValidMimeTypes_ReturnsTrue(string extension, string mimeType)
    {
        // Arrange
        var converter = new EmlConverter();
        var streamInfo = new StreamInfo(mimeType: mimeType, extension: extension);

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        result.ShouldBeTrue($"Should accept {extension} files with MIME type {mimeType}");
    }

    [Fact]
    public void EmlConverter_Priority_IsBetweenPptxAndEpub()
    {
        // Arrange
        var emlConverter = new EmlConverter();
        var epubConverter = new EpubConverter();
        var pptxConverter = new PptxConverter();

        // Act & Assert
        // Lower number = higher priority, so EML (240) should be between PPTX (230) and EPUB (250)
        emlConverter.Priority.ShouldBeGreaterThan(pptxConverter.Priority);
        emlConverter.Priority.ShouldBeLessThan(epubConverter.Priority);
    }

    [Fact]
    public async Task DocxConverter_PipelineReceivesArtifacts()
    {
        var pipeline = new RecordingPipeline("ENRICHED");
        var converter = new DocxConverter(pipeline: pipeline);

        await using var stream = DocxInlineImageFactory.Create();
        var streamInfo = new StreamInfo(
            mimeType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            extension: ".docx",
            fileName: "doc-inline-image.docx");

        var result = await converter.ConvertAsync(stream, streamInfo);

        pipeline.Executed.ShouldBeTrue();
        result.Artifacts.Images.Count.ShouldBeGreaterThan(0);
        var image = result.Artifacts.Images[0];
        image.SegmentIndex.ShouldNotBeNull();
        image.DetailedDescription.ShouldBe("ENRICHED");
        result.Segments[image.SegmentIndex!.Value].Markdown.ShouldContain("ENRICHED");
    }

    [Fact]
    public async Task PptxConverter_PipelineReceivesArtifacts()
    {
        var pipeline = new RecordingPipeline("SLIDE");
        var converter = new PptxConverter(pipeline: pipeline);

        await using var stream = TestAssetLoader.OpenAsset(TestAssetCatalog.AutogenStrategyPptx);
        var streamInfo = new StreamInfo(
            mimeType: MimeHelper.GetMimeType(".pptx"),
            extension: ".pptx",
            fileName: "autogen-strategy.pptx",
            localPath: TestAssetLoader.GetAssetPath(TestAssetCatalog.AutogenStrategyPptx));

        var result = await converter.ConvertAsync(stream, streamInfo);

        pipeline.Executed.ShouldBeTrue();
        result.Artifacts.Images.Count.ShouldBeGreaterThan(0);
        var image = result.Artifacts.Images[0];
        image.SegmentIndex.ShouldNotBeNull();
        image.DetailedDescription.ShouldBe("SLIDE");
        result.Segments[image.SegmentIndex!.Value].Markdown.ShouldContain("SLIDE");
    }
}
