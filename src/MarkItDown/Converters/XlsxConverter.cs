using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for Microsoft Excel (.xlsx) files to Markdown using DocumentFormat.OpenXml.
/// </summary>
public sealed class XlsxConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xlsx"
    };

    private static readonly HashSet<string> AcceptedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    private readonly SegmentOptions segmentOptions;

    public XlsxConverter(SegmentOptions? segmentOptions = null)
    {
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
    }

    public int Priority => 220; // Between DOCX and plain text

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

        // Validate ZIP/XLSX header if we have access to the stream
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
                    // Check for ZIP file signature (XLSX files are ZIP archives)
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

    public Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            // Reset stream position
            if (stream.CanSeek)
                stream.Position = 0;

            var segments = ExtractSegmentsFromXlsx(stream, streamInfo.FileName, cancellationToken);
            var markdown = SegmentMarkdownComposer.Compose(segments, segmentOptions);
            var title = ExtractTitle(streamInfo.FileName ?? "Excel Document");

            return Task.FromResult(new DocumentConverterResult(markdown, title, segments));
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
        {
            throw new FileConversionException($"Failed to convert XLSX file: {ex.Message}", ex);
        }
    }

    private static IReadOnlyList<DocumentSegment> ExtractSegmentsFromXlsx(Stream stream, string? fileName, CancellationToken cancellationToken)
    {
        var segments = new List<DocumentSegment>();

        using var spreadsheetDocument = SpreadsheetDocument.Open(stream, false);
        var workbookPart = spreadsheetDocument.WorkbookPart;

        if (workbookPart?.Workbook?.Sheets != null)
        {
            var sheetIndex = 0;

            foreach (var sheet in workbookPart.Workbook.Sheets.Elements<Sheet>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                sheetIndex++;
                var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
                var sheetName = sheet.Name?.Value ?? $"Sheet {sheetIndex}";
                var markdown = ConvertWorksheetToMarkdown(worksheetPart, sheetName, workbookPart);

                if (string.IsNullOrWhiteSpace(markdown))
                {
                    continue;
                }

                var metadata = new Dictionary<string, string>
                {
                    ["sheet"] = sheetIndex.ToString(CultureInfo.InvariantCulture),
                    ["sheetName"] = sheetName
                };

                segments.Add(new DocumentSegment(
                    markdown: markdown.TrimEnd(),
                    type: SegmentType.Sheet,
                    number: sheetIndex,
                    label: sheetName,
                    source: fileName,
                    additionalMetadata: metadata));
            }
        }

        return segments;
    }

    private static string ConvertWorksheetToMarkdown(WorksheetPart worksheetPart, string sheetName, WorkbookPart workbookPart)
    {
        var worksheet = worksheetPart.Worksheet;
        var sheetData = worksheet.Elements<SheetData>().FirstOrDefault();

        var result = new StringBuilder();
        result.AppendLine($"## {sheetName}");
        result.AppendLine();

        if (sheetData == null)
        {
            result.AppendLine("*No data found*");
            return result.ToString().TrimEnd();
        }

        var rows = sheetData.Elements<Row>().ToList();
        if (rows.Count == 0)
        {
            result.AppendLine("*No data found*");
            return result.ToString().TrimEnd();
        }

        var stringTable = workbookPart.SharedStringTablePart?.SharedStringTable;

        var tableData = new List<List<string>>();
        var maxColumns = 0;

        foreach (var row in rows)
        {
            var rowData = new List<string>();
            var cells = row.Elements<Cell>().ToList();

            if (cells.Count > maxColumns)
            {
                maxColumns = cells.Count;
            }

            foreach (var cell in cells)
            {
                rowData.Add(GetCellValue(cell, stringTable));
            }

            tableData.Add(rowData);
        }

        if (tableData.Count > 0 && maxColumns > 0)
        {
            foreach (var rowData in tableData)
            {
                while (rowData.Count < maxColumns)
                {
                    rowData.Add(string.Empty);
                }
            }

            CreateMarkdownTable(tableData, result);
        }
        else
        {
            result.AppendLine("*No data found*");
        }

        result.AppendLine();
        return result.ToString().TrimEnd();
    }

    private static string GetCellValue(Cell cell, SharedStringTable? stringTable)
    {
        var dataType = cell.DataType?.Value;
        var cellValue = cell.CellValue?.Text;

        if (dataType == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText ?? cell.InnerText ?? string.Empty;
        }

        if (!string.IsNullOrEmpty(cellValue))
        {
            if (dataType == CellValues.SharedString)
            {
                if (stringTable != null && int.TryParse(cellValue, out var stringIndex))
                {
                    var stringItem = stringTable.Elements<SharedStringItem>().ElementAtOrDefault(stringIndex);
                    if (stringItem is not null)
                    {
                        return stringItem.InnerText;
                    }
                }
            }
            else if (dataType == CellValues.Boolean)
            {
                return cellValue == "0" ? "FALSE" : "TRUE";
            }
            else if (dataType == CellValues.Date && double.TryParse(cellValue, out var dateValue))
            {
                return DateTime.FromOADate(dateValue).ToString("yyyy-MM-dd");
            }

            return cellValue;
        }

        if (cell.CellFormula is not null && !string.IsNullOrWhiteSpace(cell.CellFormula.Text))
        {
            return "=" + cell.CellFormula.Text.Trim();
        }

        if (cell.InlineString is not null)
        {
            return cell.InlineString.InnerText;
        }

        var innerText = cell.InnerText;
        return innerText ?? string.Empty;
    }

    private static void CreateMarkdownTable(List<List<string>> tableData, StringBuilder result)
    {
        if (tableData.Count == 0)
            return;

        var maxColumns = tableData.Max(row => row.Count);
        
        // Write header row (first row of data)
        result.Append("|");
        for (int col = 0; col < maxColumns; col++)
        {
            var cellValue = col < tableData[0].Count ? tableData[0][col] : "";
            result.Append($" {EscapeMarkdownTableCell(cellValue)} |");
        }
        result.AppendLine();

        // Write header separator
        result.Append("|");
        for (int col = 0; col < maxColumns; col++)
        {
            result.Append(" --- |");
        }
        result.AppendLine();

        // Write data rows (skip first row as it's used as header)
        for (int rowIndex = 1; rowIndex < tableData.Count; rowIndex++)
        {
            var row = tableData[rowIndex];
            result.Append("|");
            
            for (int col = 0; col < maxColumns; col++)
            {
                var cellValue = col < row.Count ? row[col] : "";
                result.Append($" {EscapeMarkdownTableCell(cellValue)} |");
            }
            result.AppendLine();
        }
    }

    private static string EscapeMarkdownTableCell(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
            
        // Escape pipe characters and trim whitespace
        return value.Replace("|", "\\|").Trim();
    }

    private static string ExtractTitle(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "Excel Document";

        // Remove file extension and return as title
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(nameWithoutExtension) ? "Excel Document" : nameWithoutExtension;
    }
}
