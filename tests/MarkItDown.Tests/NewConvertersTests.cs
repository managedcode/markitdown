using MarkItDown.Core;
using MarkItDown.Core.Converters;
using System.Text;

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
        Assert.True(result);
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
        Assert.False(result);
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
        Assert.True(result);
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
        Assert.True(result);
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
        Assert.True(result);
    }

    [Fact]
    public void ImageOcrConverter_AcceptsInput_ValidImageExtension_ReturnsTrue()
    {
        // Arrange
        var converter = new ImageOcrConverter();
        var streamInfo = new StreamInfo(mimeType: "image/jpeg", extension: ".jpg");

        // Act
        var result = converter.AcceptsInput(streamInfo);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ImageOcrConverter_AcceptsInput_MultipleImageFormats_ReturnsTrue()
    {
        // Arrange
        var converter = new ImageOcrConverter();
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
            Assert.True(result, $"Should accept {extension} files");
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
        var imageConverter = new ImageOcrConverter();

        // Assert - Ensure proper priority ordering
        Assert.Equal(200, pdfConverter.Priority);
        Assert.Equal(210, docxConverter.Priority);
        Assert.Equal(220, xlsxConverter.Priority);
        Assert.Equal(230, pptxConverter.Priority);
        Assert.Equal(500, imageConverter.Priority); // Lower priority due to resource intensity
    }

    [Theory]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData(".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    public void MarkItDown_RegistersNewConverters_CanHandleNewFormats(string extension, string mimeType)
    {
        // Arrange
        var markItDown = new MarkItDown.Core.MarkItDown();
        var registeredConverters = markItDown.GetRegisteredConverters();

        // Act
        var streamInfo = new StreamInfo(mimeType: mimeType, extension: extension);
        var canHandle = registeredConverters.Any(c => c.AcceptsInput(streamInfo));

        // Assert
        Assert.True(canHandle, $"Should have a converter that can handle {extension} files");
    }
}