using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Text;

namespace MarkItDown.Core.Converters;

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

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            // Reset stream position
            if (stream.CanSeek)
                stream.Position = 0;

            var markdown = await ExtractDataFromXlsxAsync(stream, cancellationToken);
            var title = ExtractTitle(streamInfo.FileName ?? "Excel Document");

            return new DocumentConverterResult(markdown, title);
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
        {
            throw new FileConversionException($"Failed to convert XLSX file: {ex.Message}", ex);
        }
    }

    private static async Task<string> ExtractDataFromXlsxAsync(Stream stream, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();

        await Task.Run(() =>
        {
            using var spreadsheetDocument = SpreadsheetDocument.Open(stream, false);
            var workbookPart = spreadsheetDocument.WorkbookPart;
            
            if (workbookPart?.Workbook?.Sheets != null)
            {
                foreach (var sheet in workbookPart.Workbook.Sheets.Elements<Sheet>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
                    ProcessWorksheet(worksheetPart, sheet.Name?.Value ?? "Sheet", result, workbookPart);
                }
            }
        }, cancellationToken);

        return result.ToString().Trim();
    }

    private static void ProcessWorksheet(WorksheetPart worksheetPart, string sheetName, StringBuilder result, WorkbookPart workbookPart)
    {
        var worksheet = worksheetPart.Worksheet;
        var sheetData = worksheet.Elements<SheetData>().FirstOrDefault();
        
        if (sheetData == null)
            return;

        result.AppendLine($"## {sheetName}");
        result.AppendLine();

        var rows = sheetData.Elements<Row>().ToList();
        if (rows.Count == 0)
        {
            result.AppendLine("*No data found*");
            result.AppendLine();
            return;
        }

        // Get the shared string table for string cell values
        var stringTable = workbookPart.SharedStringTablePart?.SharedStringTable;

        // Convert to table format
        var tableData = new List<List<string>>();
        var maxColumns = 0;

        foreach (var row in rows)
        {
            var rowData = new List<string>();
            var cells = row.Elements<Cell>().ToList();
            
            // Track the maximum number of columns
            if (cells.Count > maxColumns)
                maxColumns = cells.Count;

            foreach (var cell in cells)
            {
                var cellValue = GetCellValue(cell, stringTable);
                rowData.Add(cellValue);
            }
            
            tableData.Add(rowData);
        }

        // Only create a table if we have data
        if (tableData.Count > 0 && maxColumns > 0)
        {
            // Ensure all rows have the same number of columns
            foreach (var rowData in tableData)
            {
                while (rowData.Count < maxColumns)
                {
                    rowData.Add("");
                }
            }

            CreateMarkdownTable(tableData, result);
        }
        else
        {
            result.AppendLine("*No data found*");
        }
        
        result.AppendLine();
    }

    private static string GetCellValue(Cell cell, SharedStringTable? stringTable)
    {
        if (cell.CellValue == null)
            return "";

        var value = cell.CellValue.Text;
        
        if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
        {
            // Look up the value in the shared string table
            if (stringTable != null && int.TryParse(value, out var stringIndex))
            {
                var stringItem = stringTable.Elements<SharedStringItem>().ElementAtOrDefault(stringIndex);
                if (stringItem != null)
                {
                    return stringItem.InnerText;
                }
            }
        }
        else if (cell.DataType != null && cell.DataType.Value == CellValues.Boolean)
        {
            return value == "0" ? "FALSE" : "TRUE";
        }
        else if (cell.DataType != null && cell.DataType.Value == CellValues.Date)
        {
            if (double.TryParse(value, out var dateValue))
            {
                var date = DateTime.FromOADate(dateValue);
                return date.ToString("yyyy-MM-dd");
            }
        }

        return value ?? "";
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