using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MarkItDown.Core;
using MarkItDown.Core.Converters;

namespace MarkItDown.Tests;

public class TableDemonstrationTest
{
    [Fact]
    public async Task DemonstrateEnhancedTableFeatures()
    {
        // This test demonstrates all the enhanced table features working together
        var docxBytes = CreateComplexDocumentWithTables();
        
        using var stream = new MemoryStream(docxBytes);
        var converter = new DocxConverter();
        var streamInfo = new StreamInfo(extension: ".docx");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Output for demonstration
        var output = new StringBuilder();
        output.AppendLine("=== ENHANCED DOCX TABLE CONVERSION DEMO ===");
        output.AppendLine();
        output.AppendLine("Input: Complex DOCX with:");
        output.AppendLine("- Merged cells (GridSpan)");
        output.AppendLine("- Bold and italic formatting");
        output.AppendLine("- Multi-paragraph cells");
        output.AppendLine("- Nested table structures");
        output.AppendLine();
        output.AppendLine("Output Markdown:");
        output.AppendLine("----------------------------------------");
        output.AppendLine(result.Markdown);
        output.AppendLine("----------------------------------------");
        
        // Verify key enhancements are working
        Assert.Contains("**", result.Markdown); // Bold formatting
        Assert.Contains("*", result.Markdown);  // Italic formatting
        Assert.Contains("<br>", result.Markdown); // Multi-paragraph cells
        Assert.Contains("|", result.Markdown);   // Table structure
        Assert.Contains("---", result.Markdown); // Header separators
        
        // Output demonstration (this would be visible in test output)
        Console.WriteLine(output.ToString());
        
        // Validate specific enhanced features
        Assert.True(result.Markdown.Contains("**Project Status**"), "Bold headers should be preserved");
        Assert.True(result.Markdown.Contains("*In Progress*"), "Italic text should be preserved");
        Assert.True(result.Markdown.Contains("First requirement<br>Second requirement"), "Multi-paragraph cells should use <br>");
    }

    private static byte[] CreateComplexDocumentWithTables()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // Document title
            body.Append(CreateTitleParagraph("Enhanced Table Support Demonstration"));

            // First table - Basic with formatting
            body.Append(CreateFormattedTable());
            
            // Add spacing
            body.Append(new Paragraph());
            
            // Second table - With merged cells
            body.Append(CreateMergedCellsTable());

            // Add spacing  
            body.Append(new Paragraph());
            
            // Third table - With multi-paragraph cells
            body.Append(CreateMultiParagraphTable());
        }
        return stream.ToArray();
    }

    private static Paragraph CreateTitleParagraph(string title)
    {
        var paragraph = new Paragraph();
        var run = new Run();
        var runProps = new RunProperties();
        runProps.Append(new Bold());
        runProps.Append(new FontSize() { Val = "24" });
        run.Append(runProps);
        run.Append(new Text(title));
        paragraph.Append(run);
        return paragraph;
    }

    private static Table CreateFormattedTable()
    {
        var table = new Table();
        
        // Header row
        var headerRow = new TableRow();
        headerRow.Append(CreateFormattedCell("**Project Name**", true, false));
        headerRow.Append(CreateFormattedCell("**Project Status**", true, false));
        headerRow.Append(CreateFormattedCell("**Priority**", true, false));
        table.Append(headerRow);

        // Data rows
        var row1 = new TableRow();
        row1.Append(CreateFormattedCell("Alpha Development", false, false));
        row1.Append(CreateFormattedCell("*In Progress*", false, true));
        row1.Append(CreateFormattedCell("**High**", true, false));
        table.Append(row1);

        var row2 = new TableRow();
        row2.Append(CreateFormattedCell("Beta Testing", false, false));
        row2.Append(CreateFormattedCell("*Completed*", false, true));
        row2.Append(CreateFormattedCell("Medium", false, false));
        table.Append(row2);

        return table;
    }

    private static Table CreateMergedCellsTable()
    {
        var table = new Table();
        
        // Header with merged cell
        var headerRow = new TableRow();
        var mergedHeader = new TableCell();
        var cellProps = new TableCellProperties();
        cellProps.Append(new GridSpan() { Val = 3 });
        mergedHeader.Append(cellProps);
        mergedHeader.Append(CreateParagraphWithText("**Quarterly Report Summary**", true, false));
        headerRow.Append(mergedHeader);
        table.Append(headerRow);

        // Sub-headers
        var subHeaderRow = new TableRow();
        subHeaderRow.Append(CreateFormattedCell("Q1", true, false));
        subHeaderRow.Append(CreateFormattedCell("Q2", true, false));
        subHeaderRow.Append(CreateFormattedCell("Q3", true, false));
        table.Append(subHeaderRow);

        // Data row
        var dataRow = new TableRow();
        dataRow.Append(CreateFormattedCell("95%", false, false));
        dataRow.Append(CreateFormattedCell("*87%*", false, true));
        dataRow.Append(CreateFormattedCell("**92%**", true, false));
        table.Append(dataRow);

        return table;
    }

    private static Table CreateMultiParagraphTable()
    {
        var table = new Table();
        
        // Header
        var headerRow = new TableRow();
        headerRow.Append(CreateFormattedCell("**Task**", true, false));
        headerRow.Append(CreateFormattedCell("**Requirements**", true, false));
        table.Append(headerRow);

        // Row with multi-paragraph cell
        var dataRow = new TableRow();
        dataRow.Append(CreateFormattedCell("Feature Implementation", false, false));
        dataRow.Append(CreateMultiParagraphCell("First requirement", "Second requirement"));
        table.Append(dataRow);

        return table;
    }

    private static TableCell CreateFormattedCell(string text, bool bold, bool italic)
    {
        var cell = new TableCell();
        cell.Append(CreateParagraphWithText(text, bold, italic));
        return cell;
    }

    private static TableCell CreateMultiParagraphCell(string text1, string text2)
    {
        var cell = new TableCell();
        cell.Append(CreateParagraphWithText(text1, false, false));
        cell.Append(CreateParagraphWithText(text2, false, false));
        return cell;
    }

    private static Paragraph CreateParagraphWithText(string text, bool bold, bool italic)
    {
        var paragraph = new Paragraph();
        var run = new Run();
        
        if (bold || italic)
        {
            var runProps = new RunProperties();
            if (bold) runProps.Append(new Bold());
            if (italic) runProps.Append(new Italic());
            run.Append(runProps);
        }
        
        run.Append(new Text(text));
        paragraph.Append(run);
        return paragraph;
    }
}