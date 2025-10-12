using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using MarkItDown;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for Microsoft Excel (.xlsx) files to Markdown using DocumentFormat.OpenXml.
/// </summary>
public sealed class XlsxConverter : DocumentPipelineConverterBase
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
    private readonly IConversionPipeline conversionPipeline;

    public XlsxConverter(SegmentOptions? segmentOptions = null, IConversionPipeline? pipeline = null)
        : base(priority: 220)
    {
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
        conversionPipeline = pipeline ?? ConversionPipeline.Empty;
    }

    public override bool AcceptsInput(StreamInfo streamInfo)
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

    private SegmentOptions ResolveSegmentOptions()
        => ConversionContextAccessor.Current?.Segments ?? segmentOptions;

    private ArtifactStorageOptions ResolveStorageOptions()
        => ConversionContextAccessor.Current?.Storage ?? ArtifactStorageOptions.Default;

    public override bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
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

    public override async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        ArtifactWorkspace? workspace = null;
        var storageOptions = ResolveStorageOptions();
        var effectiveSegments = ResolveSegmentOptions();
        string? storedSourcePath = null;
        string? storedMarkdownPath = null;

        try
        {
            await using var source = await MaterializeSourceAsync(stream, streamInfo, ".xlsx", cancellationToken).ConfigureAwait(false);

            var defaultMime = AcceptedMimeTypes.FirstOrDefault() ?? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            workspace = WorkspacePersistence.CreateWorkspace(streamInfo, effectiveSegments, storageOptions, source.FilePath, ".xlsx", defaultMime, cancellationToken, out storedSourcePath);

            await using var fileStream = OpenReadOnlyFile(source.FilePath);
            using var spreadsheetDocument = SpreadsheetDocument.Open(fileStream, false);

            var segments = ExtractSegmentsFromXlsx(spreadsheetDocument, streamInfo.FileName, cancellationToken);
            var artifacts = BuildArtifacts(segments, streamInfo.FileName);

            await conversionPipeline.ExecuteAsync(streamInfo, artifacts, segments, cancellationToken).ConfigureAwait(false);

            var generatedAt = DateTime.UtcNow;
            var titleHint = ExtractTitle(streamInfo.FileName ?? "Excel Document");
            var meta = SegmentMarkdownComposer.Compose(segments, artifacts, streamInfo, effectiveSegments, titleHint, generatedAt);
            var title = meta.Title
                ?? titleHint
                ?? Path.GetFileNameWithoutExtension(streamInfo.FileName ?? streamInfo.LocalPath ?? streamInfo.Url)
                ?? "Spreadsheet";

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataKeys.DocumentTitle] = title,
                [MetadataKeys.DocumentPages] = segments.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentImages] = artifacts.Images.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentTables] = artifacts.Tables.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.WorkspaceDirectory] = workspace.DirectoryPath
            };

            if (!string.IsNullOrWhiteSpace(titleHint))
            {
                metadata[MetadataKeys.DocumentTitleHint] = titleHint!;
            }

            if (!string.IsNullOrWhiteSpace(storedSourcePath))
            {
                metadata[MetadataKeys.WorkspaceSourceFile] = storedSourcePath;
            }

            storedMarkdownPath = WorkspacePersistence.PersistMarkdown(workspace, storageOptions, streamInfo, meta.Markdown, cancellationToken);
            if (!string.IsNullOrWhiteSpace(storedMarkdownPath))
            {
                metadata[MetadataKeys.WorkspaceMarkdownFile] = storedMarkdownPath;
            }

            string MarkdownFactory()
            {
                var recomposed = SegmentMarkdownComposer.Compose(segments, artifacts, streamInfo, effectiveSegments, titleHint, generatedAt);
                return recomposed.Markdown;
            }

            return DocumentConverterResult.FromFactory(
                MarkdownFactory,
                title,
                segments,
                artifacts,
                metadata,
                artifactDirectory: workspace.DirectoryPath,
                cleanup: null,
                asyncCleanup: workspace,
                generatedAtUtc: generatedAt);
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
        {
            if (workspace is not null)
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
            }

            throw new FileConversionException($"Failed to convert XLSX file: {ex.Message}", ex);
        }
        catch
        {
            if (workspace is not null)
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private static List<DocumentSegment> ExtractSegmentsFromXlsx(SpreadsheetDocument spreadsheetDocument, string? fileName, CancellationToken cancellationToken)
    {
        var segments = new List<DocumentSegment>();

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
 
    private static ConversionArtifacts BuildArtifacts(IReadOnlyList<DocumentSegment> segments, string? fileName)
    {
        var artifacts = new ConversionArtifacts();
        foreach (var segment in segments)
        {
            artifacts.TextBlocks.Add(new TextArtifact(segment.Markdown, segment.Number, fileName, segment.Label));
        }

        return artifacts;
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

        var tableData = BuildTableData(sheetData, stringTable);

        if (tableData.Count > 0)
        {
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

    private static List<List<string>> BuildTableData(SheetData sheetData, SharedStringTable? stringTable)
    {
        var values = new Dictionary<(int Row, int Column), string>();
        var mergeRegions = GetMergeRegions(sheetData);
        var maxRow = mergeRegions.Count == 0 ? 0 : mergeRegions.Max(static region => region.RowEnd);
        var maxColumn = mergeRegions.Count == 0 ? 0 : mergeRegions.Max(static region => region.ColumnEnd);

        var sequentialRowIndex = 0;

        foreach (var row in sheetData.Elements<Row>())
        {
            int rowIndex;
            if (row.RowIndex is not null)
            {
                rowIndex = (int)row.RowIndex.Value;
                sequentialRowIndex = rowIndex;
            }
            else
            {
                rowIndex = ++sequentialRowIndex;
            }

            if (rowIndex > maxRow)
            {
                maxRow = rowIndex;
            }

            var nextColumn = 1;
            foreach (var cell in row.Elements<Cell>())
            {
                var reference = cell.CellReference?.Value;
                int columnIndex;
                if (!string.IsNullOrWhiteSpace(reference))
                {
                    columnIndex = GetColumnIndex(reference);
                    nextColumn = columnIndex + 1;
                }
                else
                {
                    columnIndex = nextColumn++;
                }

                if (columnIndex > maxColumn)
                {
                    maxColumn = columnIndex;
                }

                var value = (GetCellValue(cell, stringTable) ?? string.Empty).Trim();
                values[(rowIndex, columnIndex)] = value;
            }
        }

        foreach (var region in mergeRegions)
        {
            maxRow = Math.Max(maxRow, region.RowEnd);
            maxColumn = Math.Max(maxColumn, region.ColumnEnd);

            values.TryGetValue((region.RowStart, region.ColumnStart), out var value);
            value ??= string.Empty;

            for (var row = region.RowStart; row <= region.RowEnd; row++)
            {
                for (var column = region.ColumnStart; column <= region.ColumnEnd; column++)
                {
                    var key = (row, column);
                    if (!values.TryGetValue(key, out var existing) || string.IsNullOrWhiteSpace(existing))
                    {
                        values[key] = value;
                    }
                }
            }
        }

        if (maxRow == 0 || maxColumn == 0)
        {
            return new List<List<string>>();
        }

        var rows = new List<List<string>>(maxRow);
        var firstNonEmptyRow = -1;
        var lastNonEmptyRow = -1;
        var maxUsedColumn = 0;

        for (var rowIndex = 1; rowIndex <= maxRow; rowIndex++)
        {
            var rowValues = new List<string>(maxColumn);
            var rowHasContent = false;

            for (var columnIndex = 1; columnIndex <= maxColumn; columnIndex++)
            {
                values.TryGetValue((rowIndex, columnIndex), out var value);
                value ??= string.Empty;
                rowValues.Add(value);

                if (!string.IsNullOrWhiteSpace(value))
                {
                    rowHasContent = true;
                    maxUsedColumn = Math.Max(maxUsedColumn, columnIndex);
                }
            }

            if (rowHasContent)
            {
                if (firstNonEmptyRow == -1)
                {
                    firstNonEmptyRow = rowIndex;
                }

                lastNonEmptyRow = rowIndex;
            }

            rows.Add(rowValues);
        }

        if (firstNonEmptyRow == -1 || maxUsedColumn == 0)
        {
            return new List<List<string>>();
        }

        var trimmed = rows
            .Skip(firstNonEmptyRow - 1)
            .Take(lastNonEmptyRow - firstNonEmptyRow + 1)
            .Select(static row => row.ToList())
            .ToList();

        foreach (var row in trimmed)
        {
            if (row.Count > maxUsedColumn)
            {
                row.RemoveRange(maxUsedColumn, row.Count - maxUsedColumn);
            }
        }

        return trimmed;
    }

    private static IReadOnlyList<MergeRegion> GetMergeRegions(SheetData sheetData)
    {
        var mergeCells = sheetData.Parent?.Elements<MergeCells>().FirstOrDefault();
        if (mergeCells is null)
        {
            return Array.Empty<MergeRegion>();
        }

        var regions = new List<MergeRegion>();
        foreach (var mergeCell in mergeCells.Elements<MergeCell>())
        {
            var reference = mergeCell.Reference?.Value;
            if (string.IsNullOrWhiteSpace(reference))
            {
                continue;
            }

            var parts = reference.Split(':');
            if (parts.Length != 2)
            {
                continue;
            }

            var (rowStart, columnStart) = ParseReference(parts[0]);
            var (rowEnd, columnEnd) = ParseReference(parts[1]);

            if (rowStart == 0 || columnStart == 0 || rowEnd == 0 || columnEnd == 0)
            {
                continue;
            }

            regions.Add(new MergeRegion(rowStart, rowEnd, columnStart, columnEnd));
        }

        return regions;
    }

    private static (int Row, int Column) ParseReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return (0, 0);
        }

        var columnIndex = 0;
        var position = 0;
        while (position < reference.Length && char.IsLetter(reference[position]))
        {
            columnIndex = columnIndex * 26 + (char.ToUpperInvariant(reference[position]) - 'A' + 1);
            position++;
        }

        var rowIndex = 0;
        if (position < reference.Length)
        {
            var rowPart = reference[position..];
            _ = int.TryParse(rowPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out rowIndex);
        }

        return (rowIndex, columnIndex == 0 ? 1 : columnIndex);
    }

    private static int GetColumnIndex(string cellReference)
    {
        var index = 0;
        var position = 0;
        while (position < cellReference.Length && char.IsLetter(cellReference[position]))
        {
            index = index * 26 + (char.ToUpperInvariant(cellReference[position]) - 'A' + 1);
            position++;
        }

        return Math.Max(1, index);
    }

    private sealed record MergeRegion(int RowStart, int RowEnd, int ColumnStart, int ColumnEnd);
}
