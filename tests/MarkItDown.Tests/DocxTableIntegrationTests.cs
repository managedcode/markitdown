using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MarkItDown.Core;
using MarkItDown.Core.Converters;

namespace MarkItDown.Tests;

public class DocxTableIntegrationTests
{
    [Fact]
    public async Task DocxConverter_TableWithFormattedText_PreservesFormatting()
    {
        // Arrange
        var docxBytes = CreateDocxWithFormattedTable();
        using var stream = new MemoryStream(docxBytes);
        var converter = new DocxConverter();
        var streamInfo = new StreamInfo(extension: ".docx");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("|", result.Markdown); // Should contain table structure
        Assert.Contains("**Bold Text**", result.Markdown); // Should preserve bold formatting
        Assert.Contains("*Italic Text*", result.Markdown); // Should preserve italic formatting
    }

    [Fact]
    public async Task DocxConverter_TableWithMergedCells_HandlesGridSpan()
    {
        // Arrange
        var docxBytes = CreateDocxWithMergedCellsTable();
        using var stream = new MemoryStream(docxBytes);
        var converter = new DocxConverter();
        var streamInfo = new StreamInfo(extension: ".docx");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("|", result.Markdown); // Should contain table structure
        Assert.Contains("| --- |", result.Markdown); // Should contain header separators
        // The merged cell content should be present
        Assert.Contains("Merged Cell", result.Markdown);
    }

    [Fact]
    public async Task DocxConverter_TableWithMultipleParagraphs_UsesBreakTags()
    {
        // Arrange
        var docxBytes = CreateDocxWithMultiParagraphTable();
        using var stream = new MemoryStream(docxBytes);
        var converter = new DocxConverter();
        var streamInfo = new StreamInfo(extension: ".docx");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.Contains("|", result.Markdown); // Should contain table structure
        Assert.Contains("<br>", result.Markdown); // Should use <br> for paragraph breaks
    }

    private static byte[] CreateDocxWithFormattedTable()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // Create a simple table with formatted text
            var table = new Table();
            
            // Add table row 1 (header)
            var row1 = new TableRow();
            row1.Append(CreateTableCell("Header 1"));
            row1.Append(CreateTableCell("Header 2"));
            table.Append(row1);

            // Add table row 2 with formatted text
            var row2 = new TableRow();
            row2.Append(CreateFormattedTableCell("**Bold Text**", true, false));
            row2.Append(CreateFormattedTableCell("*Italic Text*", false, true));
            table.Append(row2);

            body.Append(table);
        }
        return stream.ToArray();
    }

    private static byte[] CreateDocxWithMergedCellsTable()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // Create a table with merged cells
            var table = new Table();
            
            // Add table row 1 with merged cell
            var row1 = new TableRow();
            var mergedCell = CreateTableCellWithGridSpan("Merged Cell", 2);
            row1.Append(mergedCell);
            table.Append(row1);

            // Add table row 2 with normal cells
            var row2 = new TableRow();
            row2.Append(CreateTableCell("Cell 1"));
            row2.Append(CreateTableCell("Cell 2"));
            table.Append(row2);

            body.Append(table);
        }
        return stream.ToArray();
    }

    private static byte[] CreateDocxWithMultiParagraphTable()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // Create a table with multi-paragraph cells
            var table = new Table();
            
            var row = new TableRow();
            row.Append(CreateMultiParagraphTableCell("First Paragraph", "Second Paragraph"));
            row.Append(CreateTableCell("Simple Cell"));
            table.Append(row);

            body.Append(table);
        }
        return stream.ToArray();
    }

    private static TableCell CreateTableCell(string text)
    {
        var cell = new TableCell();
        var paragraph = new Paragraph();
        var run = new Run();
        run.Append(new Text(text));
        paragraph.Append(run);
        cell.Append(paragraph);
        return cell;
    }

    private static TableCell CreateFormattedTableCell(string text, bool bold, bool italic)
    {
        var cell = new TableCell();
        var paragraph = new Paragraph();
        var run = new Run();
        
        var runProperties = new RunProperties();
        if (bold) runProperties.Append(new Bold());
        if (italic) runProperties.Append(new Italic());
        run.Append(runProperties);
        
        run.Append(new Text(text));
        paragraph.Append(run);
        cell.Append(paragraph);
        return cell;
    }

    private static TableCell CreateTableCellWithGridSpan(string text, int gridSpan)
    {
        var cell = new TableCell();
        
        // Set grid span
        var cellProperties = new TableCellProperties();
        cellProperties.Append(new GridSpan() { Val = gridSpan });
        cell.Append(cellProperties);
        
        var paragraph = new Paragraph();
        var run = new Run();
        run.Append(new Text(text));
        paragraph.Append(run);
        cell.Append(paragraph);
        return cell;
    }

    private static TableCell CreateMultiParagraphTableCell(string text1, string text2)
    {
        var cell = new TableCell();
        
        // First paragraph
        var paragraph1 = new Paragraph();
        var run1 = new Run();
        run1.Append(new Text(text1));
        paragraph1.Append(run1);
        cell.Append(paragraph1);
        
        // Second paragraph
        var paragraph2 = new Paragraph();
        var run2 = new Run();
        run2.Append(new Text(text2));
        paragraph2.Append(run2);
        cell.Append(paragraph2);
        
        return cell;
    }
}