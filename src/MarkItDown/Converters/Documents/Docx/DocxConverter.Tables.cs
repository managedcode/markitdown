using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Wordprocessing;
using MarkItDown;
using MarkItDown.Intelligence.Models;

namespace MarkItDown.Converters;

public sealed partial class DocxConverter
{
    private static string ConvertTableToMarkdown(DocumentTableResult table)
    {
        if (table.Rows.Count == 0)
        {
            return string.Empty;
        }

        var markdown = new StringBuilder();
        var header = table.Rows[0];

        markdown.Append('|');
        foreach (var cell in header)
        {
            markdown.Append(' ').Append(EscapeMarkdownTableCell(cell)).Append(" |");
        }

        markdown.AppendLine();
        markdown.Append('|');

        foreach (var _ in header)
        {
            markdown.Append(" --- |");
        }

        markdown.AppendLine();

        for (var i = 1; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            markdown.Append('|');
            foreach (var cell in row)
            {
                markdown.Append(' ').Append(EscapeMarkdownTableCell(cell)).Append(" |");
            }

            markdown.AppendLine();
        }

        return markdown.ToString();
    }

    private static (string Markdown, IList<IList<string>> RawTable) ConvertTable(Table table)
    {
        var tableData = new List<IList<string>>();
        var rows = table.Elements<TableRow>().ToList();

        if (rows.Count == 0)
        {
            return (string.Empty, tableData);
        }

        var gridColCount = table.GetFirstChild<TableGrid>()?.Elements<GridColumn>()?.Count() ?? 0;
        if (gridColCount == 0)
        {
            gridColCount = rows
                .Select(r => r.Elements<TableCell>().Sum(GetGridSpan))
                .DefaultIfEmpty(0)
                .Max();
        }

        var mergeTrack = new Dictionary<int, string>();

        foreach (var row in rows)
        {
            var expandedRow = Enumerable.Repeat(string.Empty, gridColCount).ToList();
            var colIndex = 0;

            foreach (var cell in row.Elements<TableCell>())
            {
                var cellText = ExtractCellText(cell);
                var span = GetGridSpan(cell);

                var verticalMerge = cell.TableCellProperties?.VerticalMerge;
                var isMergeContinue = verticalMerge != null &&
                                      (verticalMerge.Val is null || verticalMerge.Val.Value == MergedCellValues.Continue);

                if (isMergeContinue && mergeTrack.TryGetValue(colIndex, out var mergeValue))
                {
                    cellText = mergeValue;
                }
                else if (!string.IsNullOrWhiteSpace(cellText))
                {
                    mergeTrack[colIndex] = cellText;
                }
                else
                {
                    mergeTrack.Remove(colIndex);
                }

                for (var s = 0; s < span && colIndex < gridColCount; s++)
                {
                    expandedRow[colIndex++] = cellText;
                }
            }

            tableData.Add(expandedRow);
        }

        if (!tableData.Any())
        {
            return (string.Empty, tableData);
        }

        PropagateEmptyCells(tableData, startRow: 1);

        var markdown = new StringBuilder();
        var headerRow = tableData[0];

        markdown.Append('|');
        foreach (var cell in headerRow)
        {
            markdown.Append(' ').Append(EscapeMarkdownTableCell(cell)).Append(" |");
        }
        markdown.AppendLine();

        markdown.Append('|');
        foreach (var _ in headerRow)
        {
            markdown.Append(" --- |");
        }
        markdown.AppendLine();

        for (var i = 1; i < tableData.Count; i++)
        {
            var row = tableData[i];
            markdown.Append('|');
            foreach (var cell in row)
            {
                markdown.Append(' ').Append(EscapeMarkdownTableCell(cell)).Append(" |");
            }
            markdown.AppendLine();
        }

        return (markdown.ToString(), tableData);
    }

    private ElementProcessingResult ProcessTable(TableDescriptor descriptor)
    {
        var (markdown, rows) = ConvertTable(descriptor.Table);
        var breakCount = CountPageBreaks(descriptor.Table, cloneConsumesBreak: true);
        var pageSpan = Math.Max(1, breakCount + 1);

        if (pageSpan > 1)
        {
            var start = descriptor.PageNumber;
            var end = start + pageSpan - 1;
            var header = $"<!-- Table spans pages {start}-{end} -->\n";
            markdown = string.Concat(header, markdown);
        }

        return new ElementProcessingResult(
            descriptor.Index,
            descriptor.PageNumber,
            pageSpan,
            DocxElementKind.Table,
            markdown,
            markdown,
            rows,
            Array.Empty<ImageArtifact>());
    }

    private static void PropagateEmptyCells(IList<IList<string>> tableData, int startRow)
    {
        if (tableData.Count <= startRow)
        {
            return;
        }

        var columnCount = tableData[startRow].Count;
        for (var col = 0; col < columnCount; col++)
        {
            string? lastValue = null;

            for (var row = startRow; row < tableData.Count; row++)
            {
                var value = tableData[row][col];
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (!string.IsNullOrWhiteSpace(lastValue))
                    {
                        tableData[row][col] = lastValue!;
                    }
                }
                else
                {
                    lastValue = value;
                }
            }
        }
    }

    private static string ExtractCellText(TableCell cell)
    {
        var paragraphs = new List<string>();

        foreach (var paragraph in cell.Elements<Paragraph>())
        {
            var text = CleanText(paragraph.InnerText);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (paragraphs.Count == 0 || !string.Equals(paragraphs[^1], text, StringComparison.Ordinal))
            {
                paragraphs.Add(text);
            }
        }

        if (paragraphs.Count == 0)
        {
            return CleanText(cell.InnerText);
        }

        var combined = string.Join('\n', paragraphs);
        return TextSanitizer.Normalize(combined, trim: true, collapseWhitespaceAroundLineBreaks: false);
    }

}
