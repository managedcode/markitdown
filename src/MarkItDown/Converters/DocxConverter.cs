using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for Microsoft Word (.docx) files to Markdown using DocumentFormat.OpenXml.
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
        
        var isFirstRow = true;
        foreach (var row in rows)
        {
            var cells = row.Elements<TableCell>().ToList();
            if (cells.Count == 0)
                continue;

            result.Append("|");
            foreach (var cell in cells)
            {
                var cellText = ExtractCellText(cell);
                result.Append($" {cellText.Replace("|", "\\|").Trim()} |");
            }
            result.AppendLine();

            // Add header separator after first row
            if (isFirstRow)
            {
                result.Append("|");
                for (int i = 0; i < cells.Count; i++)
                {
                    result.Append(" --- |");
                }
                result.AppendLine();
                isFirstRow = false;
            }
        }
        
        result.AppendLine();
    }

    private static string ExtractCellText(TableCell cell)
    {
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
            
            if (cellText.Length > 0)
                cellText.Append(" ");
        }
        
        return cellText.ToString().Trim();
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