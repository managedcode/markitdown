using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MarkItDown.Intelligence;

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
    private readonly IConversionPipeline conversionPipeline;
    private readonly IImageUnderstandingProvider? imageUnderstandingProvider;

    public DocxConverter(SegmentOptions? segmentOptions = null, IConversionPipeline? pipeline = null, IImageUnderstandingProvider? imageProvider = null)
    {
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
        conversionPipeline = pipeline ?? ConversionPipeline.Empty;
        imageUnderstandingProvider = imageProvider;
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

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            if (stream.CanSeek)
                stream.Position = 0;

            var extraction = await ExtractDocumentAsync(stream, streamInfo, cancellationToken).ConfigureAwait(false);
            await conversionPipeline.ExecuteAsync(streamInfo, extraction.Artifacts, extraction.Segments, cancellationToken).ConfigureAwait(false);

            var markdown = SegmentMarkdownComposer.Compose(extraction.Segments, segmentOptions);
            var title = ExtractTitle(extraction.RawText);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = ExtractTitle(markdown);
            }

            return new DocumentConverterResult(markdown, title, extraction.Segments, extraction.Artifacts);
        }
        catch (Exception ex) when (ex is not MarkItDownException)
        {
            throw new FileConversionException($"Failed to convert DOCX file: {ex.Message}", ex);
        }
    }

    private sealed class DocxExtractionResult
    {
        public DocxExtractionResult(List<DocumentSegment> segments, ConversionArtifacts artifacts, string rawText)
        {
            Segments = segments;
            Artifacts = artifacts;
            RawText = rawText;
        }

        public List<DocumentSegment> Segments { get; }

        public ConversionArtifacts Artifacts { get; }

        public string RawText { get; }
    }

    private sealed record PageContent(int PageNumber, string Markdown);

    private async Task<DocxExtractionResult> ExtractDocumentAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        using var wordDocument = WordprocessingDocument.Open(stream, false);
        var body = wordDocument.MainDocumentPart?.Document?.Body;

        if (body is null)
        {
            return new DocxExtractionResult(new List<DocumentSegment>(), new ConversionArtifacts(), string.Empty);
        }

        var artifacts = new ConversionArtifacts();
        var pages = new List<PageContent>();
        var pageBuilder = new StringBuilder();
        var rawTextBuilder = new StringBuilder();
        var imagesByPage = new Dictionary<int, List<ImageArtifact>>();
        var pageNumber = 1;
        var tableCount = 0;
        var imageCount = 0;

        foreach (var element in body.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (element)
            {
                case Paragraph paragraph:
                {
                    if (ContainsPageBreak(paragraph))
                    {
                        FinalizePage(pages, pageBuilder, ref pageNumber, rawTextBuilder);
                    }

                    var paragraphMarkdown = ConvertParagraph(paragraph);
                    if (!string.IsNullOrEmpty(paragraphMarkdown))
                    {
                        pageBuilder.AppendLine(paragraphMarkdown);
                        pageBuilder.AppendLine();
                    }

                    if (wordDocument.MainDocumentPart is not null)
                    {
                        foreach (var drawing in paragraph.Descendants<DocumentFormat.OpenXml.Wordprocessing.Drawing>())
                        {
                            var artifact = await ExtractImageAsync(drawing, wordDocument.MainDocumentPart, pageNumber, streamInfo, cancellationToken).ConfigureAwait(false);
                            if (artifact is null)
                            {
                                continue;
                            }

                            imageCount++;
                            artifact.Label ??= $"Image {imageCount}";
                            AppendImagePlaceholder(pageBuilder, artifact);
                            AddImageToPage(imagesByPage, pageNumber, artifact);
                        }
                    }

                    break;
                }

                case Table table:
                {
                    var (tableMarkdown, rawTable) = ConvertTable(table);
                    if (!string.IsNullOrWhiteSpace(tableMarkdown))
                    {
                        pageBuilder.AppendLine(tableMarkdown.TrimEnd());
                        pageBuilder.AppendLine();
                        tableCount++;
                        artifacts.Tables.Add(new TableArtifact(rawTable, pageNumber, streamInfo.FileName, $"Table {tableCount}"));
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

        FinalizePage(pages, pageBuilder, ref pageNumber, rawTextBuilder);

        var segments = new List<DocumentSegment>();

        foreach (var page in pages)
        {
            var segment = CreatePageSegment(page.Markdown, page.PageNumber, streamInfo.FileName);
            var index = segments.Count;
            segments.Add(segment);
            artifacts.TextBlocks.Add(new TextArtifact(page.Markdown, page.PageNumber, streamInfo.FileName, segment.Label));

            if (imagesByPage.TryGetValue(page.PageNumber, out var pageImages))
            {
                foreach (var artifact in pageImages)
                {
                    artifact.SegmentIndex = index;
                    artifacts.Images.Add(artifact);
                }
            }
        }

        return new DocxExtractionResult(segments, artifacts, rawTextBuilder.ToString().Trim());
    }

    private static void FinalizePage(ICollection<PageContent> pages, StringBuilder builder, ref int pageNumber, StringBuilder rawTextBuilder)
    {
        if (builder.Length == 0)
        {
            pageNumber++;
            return;
        }

        var markdown = builder.ToString().TrimEnd();
        if (!string.IsNullOrEmpty(markdown))
        {
            pages.Add(new PageContent(pageNumber, markdown));

            if (rawTextBuilder.Length > 0)
            {
                rawTextBuilder.AppendLine();
            }

            rawTextBuilder.AppendLine(markdown);
        }

        pageNumber++;
        builder.Clear();
    }

    private static void AppendImagePlaceholder(StringBuilder builder, ImageArtifact artifact)
    {
        var mimeType = string.IsNullOrWhiteSpace(artifact.ContentType) ? "image/png" : artifact.ContentType;
        var base64 = Convert.ToBase64String(artifact.Data);
        var label = artifact.Label ?? "Document image";
        var placeholder = $"![{label}](data:{mimeType};base64,{base64})";

        artifact.PlaceholderMarkdown = placeholder;
        builder.AppendLine(placeholder);
        builder.AppendLine();
    }

    private static void AddImageToPage(IDictionary<int, List<ImageArtifact>> imagesByPage, int pageNumber, ImageArtifact artifact)
    {
        if (!imagesByPage.TryGetValue(pageNumber, out var list))
        {
            list = new List<ImageArtifact>();
            imagesByPage[pageNumber] = list;
        }

        list.Add(artifact);
    }

    private async Task<ImageArtifact?> ExtractImageAsync(DocumentFormat.OpenXml.Wordprocessing.Drawing drawing, MainDocumentPart mainDocumentPart, int pageNumber, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        var blip = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
        if (blip?.Embed?.Value is not string relationshipId)
        {
            return null;
        }

        if (mainDocumentPart.GetPartById(relationshipId) is not ImagePart imagePart)
        {
            return null;
        }

        using var partStream = imagePart.GetStream();
        using var memory = new MemoryStream();
        await partStream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        var data = memory.ToArray();

        var artifact = new ImageArtifact(data, imagePart.ContentType, pageNumber, streamInfo.FileName);
        artifact.Metadata[MetadataKeys.Page] = pageNumber.ToString(CultureInfo.InvariantCulture);

        if (imageUnderstandingProvider is not null)
        {
            try
            {
                using var analysisStream = new MemoryStream(data, writable: false);
                var result = await imageUnderstandingProvider.AnalyzeAsync(analysisStream, streamInfo, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(result?.Caption))
                {
                    artifact.Metadata[MetadataKeys.Caption] = result!.Caption!;
                }

                if (!string.IsNullOrWhiteSpace(result?.Text))
                {
                    artifact.Metadata["ocrText"] = result!.Text!;
                    artifact.RawText = result.Text;
                }
            }
            catch
            {
                // Ignore provider failures to keep extraction resilient.
            }
        }

        return artifact;
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
            return (string.Empty, tableData);
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

        return (markdown.ToString(), tableData);
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
