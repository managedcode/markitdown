using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;

namespace MarkItDown.Core.Converters;

/// <summary>
/// Converter for Microsoft Word (.docx) files to Markdown using DocumentFormat.OpenXml.
/// 
/// Enhanced table support includes:
/// - Merged cells (horizontal spanning via GridSpan)
/// - Formatting preservation (bold, italic) in cell content
/// - Multi-paragraph cells using &lt;br&gt; tags
/// - Nested table handling with simplified representation
/// - Proper table structure analysis for complex layouts
/// </summary>
public sealed class DocxConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx"
    };

    private static readonly HashSet<string> AcceptedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    public int Priority => 210; // Between PDF and plain text

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var mimeType = streamInfo.MimeType?.ToLowerInvariant() ?? string.Empty;
        var extension = streamInfo.Extension?.ToLowerInvariant();

        // Check the extension
        if (extension is not null && AcceptedExtensions.Contains(extension))
            return true;

        // Check the mimetype
        if (AcceptedMimeTypes.Contains(mimeType))
            return true;

        return false;
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (!AcceptsInput(streamInfo))
            return false;

        // Validate ZIP/DOCX header if we have access to the stream
        if (stream.CanSeek && stream.Length > 4)
        {
            var originalPosition = stream.Position;
            try
            {
                stream.Position = 0;
                var buffer = new byte[4];
                var bytesRead = stream.Read(buffer, 0, 4);
                stream.Position = originalPosition;

                if (bytesRead == 4)
                {
                    // Check for ZIP file signature (DOCX files are ZIP archives)
                    return buffer[0] == 0x50 && buffer[1] == 0x4B && 
                           (buffer[2] == 0x03 || buffer[2] == 0x05 || buffer[2] == 0x07) && 
                           (buffer[3] == 0x04 || buffer[3] == 0x06 || buffer[3] == 0x08);
                }
            }
            catch
            {
                stream.Position = originalPosition;
            }
        }

        return true;
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            // Reset stream position
            if (stream.CanSeek)
                stream.Position = 0;

            var markdown = await ExtractTextFromDocxAsync(stream, cancellationToken);
            var title = ExtractTitle(markdown);

            return new DocumentConverterResult(markdown, title);
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
        {
            throw new FileConversionException($"Failed to convert DOCX file: {ex.Message}", ex);
        }
    }

    private static async Task<string> ExtractTextFromDocxAsync(Stream stream, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();

        await Task.Run(() =>
        {
            using var wordDocument = WordprocessingDocument.Open(stream, false);
            var body = wordDocument.MainDocumentPart?.Document?.Body;

            if (body != null)
            {
                ProcessBodyElements(body, result, cancellationToken);
            }
        }, cancellationToken);

        return result.ToString().Trim();
    }

    private static void ProcessBodyElements(Body body, StringBuilder result, CancellationToken cancellationToken)
    {
        foreach (var element in body.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (element)
            {
                case Paragraph paragraph:
                    ProcessParagraph(paragraph, result);
                    break;
                case Table table:
                    ProcessTable(table, result);
                    break;
                // Add more element types as needed
            }
        }
    }

    private static void ProcessParagraph(Paragraph paragraph, StringBuilder result)
    {
        var paragraphText = new StringBuilder();
        var isHeading = false;
        var headingLevel = 0;

        // Check paragraph properties for heading styles
        var paragraphProperties = paragraph.ParagraphProperties;
        if (paragraphProperties?.ParagraphStyleId?.Val?.Value != null)
        {
            var styleId = paragraphProperties.ParagraphStyleId.Val.Value.ToLowerInvariant();
            if (styleId.StartsWith("heading"))
            {
                isHeading = true;
                if (int.TryParse(styleId.Replace("heading", ""), out var level))
                {
                    headingLevel = level;
                }
            }
        }

        // Process runs within the paragraph
        foreach (var run in paragraph.Elements<Run>())
        {
            var runProperties = run.RunProperties;
            var currentBold = runProperties?.Bold != null;
            var currentItalic = runProperties?.Italic != null;

            foreach (var textElement in run.Elements())
            {
                switch (textElement)
                {
                    case Text text:
                        var textContent = text.Text;
                        
                        // Apply formatting
                        if (currentBold && !isHeading)
                            textContent = $"**{textContent}**";
                        if (currentItalic && !isHeading)
                            textContent = $"*{textContent}*";
                            
                        paragraphText.Append(textContent);
                        break;
                    case TabChar:
                        paragraphText.Append("\t");
                        break;
                    case Break:
                        paragraphText.AppendLine();
                        break;
                }
            }
        }

        var finalText = paragraphText.ToString();
        
        if (!string.IsNullOrWhiteSpace(finalText))
        {
            if (isHeading && headingLevel > 0)
            {
                result.Append(new string('#', Math.Min(headingLevel, 6)));
                result.Append(' ');
                result.AppendLine(finalText.Trim());
                result.AppendLine();
            }
            else
            {
                result.AppendLine(finalText.Trim());
                result.AppendLine();
            }
        }
    }

    private static void ProcessTable(Table table, StringBuilder result)
    {
        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count == 0)
            return;

        result.AppendLine();
        
        // Analyze table structure for better handling
        var tableStructure = AnalyzeTableStructure(table, rows);
        
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var cells = row.Elements<TableCell>().ToList();
            if (cells.Count == 0)
                continue;

            var processedRow = ProcessTableRow(row, cells, tableStructure, rowIndex);
            if (!string.IsNullOrEmpty(processedRow))
            {
                result.Append(processedRow);
                result.AppendLine();
                
                // Add header separator after first row or after identified header rows
                if (rowIndex == 0 || (tableStructure.HasHeaderRows && rowIndex == tableStructure.HeaderRowCount - 1))
                {
                    var separatorRow = CreateHeaderSeparator(cells.Count, tableStructure);
                    result.Append(separatorRow);
                    result.AppendLine();
                }
            }
        }
        
        result.AppendLine();
    }

    private static TableStructureInfo AnalyzeTableStructure(Table table, List<TableRow> rows)
    {
        var structure = new TableStructureInfo();
        
        // Determine if first row(s) are headers by checking for table header properties
        if (rows.Count > 0)
        {
            var firstRow = rows[0];
            var firstRowProperties = firstRow.TableRowProperties;
            
            // Check if first row is marked as header using TableRowHeight or other indicators
            // For now, we'll use a heuristic: if the first row has different formatting, treat as header
            structure.HasHeaderRows = true; // Default to treating first row as header
            structure.HeaderRowCount = 1;
        }
        
        // Determine maximum column count considering merged cells
        structure.MaxColumns = 0;
        foreach (var row in rows)
        {
            int columnCount = 0;
            foreach (var cell in row.Elements<TableCell>())
            {
                var gridSpan = GetGridSpan(cell);
                columnCount += gridSpan;
            }
            structure.MaxColumns = Math.Max(structure.MaxColumns, columnCount);
        }
        
        return structure;
    }

    private static string ProcessTableRow(TableRow row, List<TableCell> cells, TableStructureInfo structure, int rowIndex)
    {
        var result = new StringBuilder();
        result.Append("|");
        
        int currentColumn = 0;
        
        foreach (var cell in cells)
        {
            var cellText = ExtractCellText(cell);
            var gridSpan = GetGridSpan(cell);
            var vMerge = GetVerticalMerge(cell);
            
            // Handle merged cells by escaping pipes and adding appropriate content
            cellText = cellText.Replace("|", "\\|").Trim();
            
            // For horizontally merged cells, we still show the content but note the span
            if (gridSpan > 1)
            {
                // Add the cell content
                result.Append($" {cellText} |");
                
                // Add empty cells for the spanned columns
                for (int i = 1; i < gridSpan; i++)
                {
                    result.Append(" |");
                }
                currentColumn += gridSpan;
            }
            else
            {
                result.Append($" {cellText} |");
                currentColumn++;
            }
        }
        
        // Fill remaining columns if this row is shorter than the max
        while (currentColumn < structure.MaxColumns)
        {
            result.Append(" |");
            currentColumn++;
        }
        
        return result.ToString();
    }

    private static string CreateHeaderSeparator(int cellCount, TableStructureInfo structure)
    {
        var result = new StringBuilder();
        result.Append("|");
        
        for (int i = 0; i < structure.MaxColumns; i++)
        {
            result.Append(" --- |");
        }
        
        return result.ToString();
    }

    private static int GetGridSpan(TableCell cell)
    {
        var tcPr = cell.TableCellProperties;
        var gridSpan = tcPr?.GridSpan;
        return gridSpan?.Val?.Value ?? 1;
    }

    private static MergedCellValues? GetVerticalMerge(TableCell cell)
    {
        var tcPr = cell.TableCellProperties;
        var vMerge = tcPr?.VerticalMerge;
        return vMerge?.Val?.Value;
    }

    private class TableStructureInfo
    {
        public bool HasHeaderRows { get; set; }
        public int HeaderRowCount { get; set; } = 0;
        public int MaxColumns { get; set; }
    }

    private static string ExtractCellText(TableCell cell)
    {
        var cellText = new StringBuilder();
        
        // Process all elements in the cell, not just paragraphs
        foreach (var element in cell.Elements())
        {
            switch (element)
            {
                case Paragraph paragraph:
                    var paragraphText = ExtractParagraphText(paragraph);
                    if (!string.IsNullOrEmpty(paragraphText))
                    {
                        if (cellText.Length > 0)
                            cellText.Append("<br>");
                        cellText.Append(paragraphText);
                    }
                    break;
                    
                case Table nestedTable:
                    // Handle nested tables by converting them to a simplified format
                    var nestedTableText = ExtractNestedTableText(nestedTable);
                    if (!string.IsNullOrEmpty(nestedTableText))
                    {
                        if (cellText.Length > 0)
                            cellText.Append("<br>");
                        cellText.Append(nestedTableText);
                    }
                    break;
                    
                // Add other element types as needed
            }
        }
        
        return cellText.ToString().Trim();
    }

    private static string ExtractParagraphText(Paragraph paragraph)
    {
        var paragraphText = new StringBuilder();
        
        foreach (var run in paragraph.Elements<Run>())
        {
            var runProperties = run.RunProperties;
            var isBold = runProperties?.Bold != null;
            var isItalic = runProperties?.Italic != null;
            
            foreach (var textElement in run.Elements())
            {
                string textContent = string.Empty;
                
                switch (textElement)
                {
                    case Text text:
                        textContent = text.Text;
                        break;
                    case TabChar:
                        textContent = " "; // Convert tabs to spaces in tables
                        break;
                    case Break:
                        textContent = " "; // Convert line breaks to spaces within cells
                        break;
                }
                
                // Apply formatting
                if (!string.IsNullOrEmpty(textContent))
                {
                    if (isBold)
                        textContent = $"**{textContent}**";
                    if (isItalic)
                        textContent = $"*{textContent}*";
                        
                    paragraphText.Append(textContent);
                }
            }
        }
        
        return paragraphText.ToString().Trim();
    }

    private static string ExtractNestedTableText(Table nestedTable)
    {
        // For nested tables, create a simplified representation
        var result = new StringBuilder();
        var rows = nestedTable.Elements<TableRow>().ToList();
        
        if (rows.Count == 0)
            return string.Empty;
            
        result.Append("[Table: ");
        
        // Extract just the text content in a simplified format
        for (int i = 0; i < Math.Min(rows.Count, 3); i++) // Limit to first 3 rows
        {
            var row = rows[i];
            var cells = row.Elements<TableCell>().ToList();
            
            if (i > 0)
                result.Append(" | ");
                
            for (int j = 0; j < Math.Min(cells.Count, 3); j++) // Limit to first 3 cells
            {
                if (j > 0)
                    result.Append(", ");
                    
                var cellText = ExtractSimpleCellText(cells[j]);
                result.Append(cellText);
            }
            
            if (cells.Count > 3)
                result.Append("...");
        }
        
        if (rows.Count > 3)
            result.Append("...");
            
        result.Append("]");
        return result.ToString();
    }

    private static string ExtractSimpleCellText(TableCell cell)
    {
        // Simplified extraction for nested tables to avoid complexity
        var cellText = new StringBuilder();
        
        foreach (var paragraph in cell.Elements<Paragraph>())
        {
            foreach (var run in paragraph.Elements<Run>())
            {
                foreach (var text in run.Elements<Text>())
                {
                    cellText.Append(text.Text);
                }
            }
        }
        
        var result = cellText.ToString().Trim();
        return result.Length > 30 ? result.Substring(0, 30) + "..." : result;
    }

    private static string? ExtractTitle(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return null;

        var lines = markdown.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        // Look for the first heading
        foreach (var line in lines.Take(10))
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith('#'))
            {
                return trimmedLine.TrimStart('#').Trim();
            }
        }

        // If no heading found, use the first substantial line
        foreach (var line in lines.Take(5))
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.Length > 5 && trimmedLine.Length < 100)
            {
                return trimmedLine;
            }
        }

        return null;
    }
}