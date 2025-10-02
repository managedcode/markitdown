using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

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

    private readonly SegmentOptions segmentOptions;

    public DocxConverter(SegmentOptions? segmentOptions = null)
    {
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
    }

    public int Priority => 210; // Between PDF and plain text

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var mimeType = streamInfo.MimeType?.ToLowerInvariant() ?? string.Empty;
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && AcceptedExtensions.Contains(extension))
            return true;

        if (AcceptedMimeTypes.Contains(mimeType))
            return true;

        return false;
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (!AcceptsInput(streamInfo))
            return false;

        if (stream.CanSeek && stream.Length > 4)
        {
            var originalPosition = stream.Position;
            try
            {
                stream.Position = 0;
                Span<byte> buffer = stackalloc byte[4];
                var bytesRead = stream.Read(buffer);
                stream.Position = originalPosition;

                if (bytesRead == 4)
                {
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
            if (stream.CanSeek)
                stream.Position = 0;

            var segments = ExtractDocumentSegments(stream, streamInfo.FileName, cancellationToken);
            var markdown = SegmentMarkdownComposer.Compose(segments, segmentOptions);
            var title = ExtractTitle(markdown);

            return Task.FromResult(new DocumentConverterResult(markdown, title, segments));
        }
        catch (Exception ex) when (ex is not MarkItDownException)
        {
            throw new FileConversionException($"Failed to convert DOCX file: {ex.Message}", ex);
        }
    }

    private IReadOnlyList<DocumentSegment> ExtractDocumentSegments(Stream stream, string? fileName, CancellationToken cancellationToken)
    {
        using var wordDocument = WordprocessingDocument.Open(stream, false);
        var body = wordDocument.MainDocumentPart?.Document?.Body;

        if (body is null)
        {
            return Array.Empty<DocumentSegment>();
        }

        var segments = new List<DocumentSegment>();
        var pageBuilder = new StringBuilder();
        var pageNumber = 1;
        var source = fileName;

        foreach (var element in body.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (element)
            {
                case Paragraph paragraph:
                {
                    if (ContainsPageBreak(paragraph))
                    {
                        FinalizePageSegment(segments, pageBuilder, ref pageNumber, source);
                    }

                    var paragraphMarkdown = ConvertParagraph(paragraph);
                    if (!string.IsNullOrEmpty(paragraphMarkdown))
                    {
                        pageBuilder.AppendLine(paragraphMarkdown);
                        pageBuilder.AppendLine();
                    }

                    break;
                }

                case Table table:
                {
                    var tableMarkdown = ConvertTableToMarkdown(table);
                    if (!string.IsNullOrWhiteSpace(tableMarkdown))
                    {
                        pageBuilder.AppendLine(tableMarkdown.TrimEnd());
                        pageBuilder.AppendLine();
                    }

                    break;
                }

                default:
                {
                    var text = CleanText(element.InnerText ?? string.Empty);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        pageBuilder.AppendLine(text);
                        pageBuilder.AppendLine();
                    }

                    break;
                }
            }
        }

        if (pageBuilder.Length > 0)
        {
            var markdown = pageBuilder.ToString().TrimEnd();
            if (!string.IsNullOrEmpty(markdown))
            {
                segments.Add(CreatePageSegment(markdown, pageNumber, source));
            }
        }

        return segments;
    }

    private static void FinalizePageSegment(List<DocumentSegment> segments, StringBuilder builder, ref int pageNumber, string? source)
    {
        var markdown = builder.ToString().TrimEnd();
        if (!string.IsNullOrEmpty(markdown))
        {
            segments.Add(CreatePageSegment(markdown, pageNumber, source));
        }

        pageNumber++;
        builder.Clear();
    }

    private static DocumentSegment CreatePageSegment(string markdown, int pageNumber, string? source)
    {
        var metadata = new Dictionary<string, string>
        {
            ["page"] = pageNumber.ToString(CultureInfo.InvariantCulture)
        };

        return new DocumentSegment(
            markdown: markdown,
            type: SegmentType.Page,
            number: pageNumber,
            label: $"Page {pageNumber}",
            source: source,
            additionalMetadata: metadata);
    }

    private static bool ContainsPageBreak(Paragraph paragraph)
        => paragraph.Descendants<LastRenderedPageBreak>().Any() ||
           paragraph.Descendants<Break>().Any(b => b.Type?.Value == BreakValues.Page);

    private static string ConvertParagraph(Paragraph paragraph)
    {
        var paragraphText = new StringBuilder();
        var isHeading = false;
        var headingLevel = 0;

        var paragraphProperties = paragraph.ParagraphProperties;
        if (paragraphProperties?.ParagraphStyleId?.Val?.Value is string styleId)
        {
            styleId = styleId.ToLowerInvariant();
            if (styleId.StartsWith("heading", StringComparison.Ordinal))
            {
                isHeading = true;
                if (int.TryParse(styleId.Replace("heading", string.Empty, StringComparison.Ordinal), out var level))
                {
                    headingLevel = Math.Clamp(level, 1, 6);
                }
            }
        }

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
                    {
                        var textContent = text.Text;
                        if (string.IsNullOrEmpty(textContent))
                        {
                            continue;
                        }

                        if (currentBold && !isHeading)
                        {
                            textContent = $"**{textContent}**";
                        }

                        if (currentItalic && !isHeading)
                        {
                            textContent = $"*{textContent}*";
                        }

                        paragraphText.Append(textContent);
                        break;
                    }
                    case TabChar:
                        paragraphText.Append('\t');
                        break;
                    case Break br when br.Type?.Value == BreakValues.TextWrapping:
                        paragraphText.AppendLine();
                        break;
                }
            }
        }

        var finalText = paragraphText.ToString().Trim();
        if (string.IsNullOrWhiteSpace(finalText))
        {
            return string.Empty;
        }

        if (isHeading && headingLevel > 0)
        {
            return $"{new string('#', headingLevel)} {finalText}";
        }

        return finalText;
    }

    private static string ConvertTableToMarkdown(Table table)
    {
        var tableData = new List<List<string>>();
        var rows = table.Elements<TableRow>().ToList();

        if (rows.Count == 0)
        {
            return string.Empty;
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
                var cellText = CleanText(cell.InnerText);
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
            return string.Empty;
        }

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

        return markdown.ToString();
    }

    private static int GetGridSpan(TableCell cell)
    {
        var span = cell.TableCellProperties?.GridSpan?.Val;
        return span is not null && span.HasValue ? span.Value : 1;
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Replace('\r', '\n')
                   .Replace("\n\n", "\n")
                   .Trim();
    }

    private static string EscapeMarkdownTableCell(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Replace("|", "\\|").Trim();
    }

    private static string? ExtractTitle(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return null;

        var lines = markdown.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines.Take(10))
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith('#'))
            {
                return trimmedLine.TrimStart('#').Trim();
            }
        }

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
