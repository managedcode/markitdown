using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MarkItDown;

namespace MarkItDown.Converters;

public sealed partial class DocxConverter
{
    private ElementProcessingResult ProcessOther(OtherDescriptor descriptor)
    {
        var text = CleanText(descriptor.Element.InnerText ?? string.Empty);

        return new ElementProcessingResult(
            descriptor.Index,
            descriptor.PageNumber,
            PageSpan: 1,
            DocxElementKind.Other,
            text,
            text,
            null,
            Array.Empty<ImageArtifact>());
    }

    private async Task<DocxExtractionResult> ExtractDocumentAsync(
        string docxPath,
        StreamInfo streamInfo,
        ImageArtifactPersistor imagePersistor,
        SegmentOptions options,
        CancellationToken cancellationToken)
    {
        using var wordDocument = OpenWordprocessingDocument(docxPath);
        var body = wordDocument.MainDocumentPart?.Document?.Body;

        if (body is null)
        {
            return new DocxExtractionResult(new List<DocumentSegment>(), new ConversionArtifacts(), string.Empty);
        }

        var descriptors = BuildDescriptors(body, cancellationToken);
        ReportDocxProgress("docx-parser", descriptors.Count > 0 ? 1 : 0, 1, $"strategy=openxml-body elements={descriptors.Count}");
        if (descriptors.Count == 0)
        {
            return new DocxExtractionResult(new List<DocumentSegment>(), new ConversionArtifacts(), string.Empty);
        }

        var imageCatalog = BuildImageCatalog(wordDocument, cancellationToken);
        var artifacts = new ConversionArtifacts();
        var results = new ElementProcessingResult[descriptors.Count];

        var maxDegree = options.MaxParallelImageAnalysis;
        if (maxDegree <= 0)
        {
            throw new InvalidOperationException("SegmentOptions.MaxParallelImageAnalysis must be positive.");
        }

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = maxDegree
        };

        await Parallel.ForEachAsync(descriptors, parallelOptions, (descriptor, token) =>
        {
            ElementProcessingResult result = descriptor switch
            {
                ParagraphDescriptor paragraphDescriptor => ProcessParagraph(paragraphDescriptor, imageCatalog, streamInfo, imagePersistor, token),
                TableDescriptor tableDescriptor => ProcessTable(tableDescriptor),
                OtherDescriptor otherDescriptor => ProcessOther(otherDescriptor),
                _ => throw new InvalidOperationException("Unsupported descriptor type.")
            };

            results[descriptor.Index] = result;
            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);

        var pageMap = new SortedDictionary<int, PageAccumulator>();
        var tableCount = 0;

        foreach (var result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var accumulator = GetOrCreatePageAccumulator(pageMap, result.PageNumber);

            if (!string.IsNullOrWhiteSpace(result.Markdown))
            {
                AppendMarkdown(accumulator.Markdown, result.Markdown);
            }

            if (result.Images.Count > 0)
            {
                accumulator.Images.AddRange(result.Images);
            }

            string? tableLabel = null;

            if (result.Kind == DocxElementKind.Table &&
                result.TableRows is not null &&
                result.TableRows.Count > 0)
            {
                tableCount++;
                tableLabel = $"Table {tableCount}";
                var tableArtifact = new TableArtifact(result.TableRows, result.PageNumber, streamInfo.FileName, tableLabel);
                tableArtifact.Metadata[MetadataKeys.TableIndex] = tableCount.ToString(CultureInfo.InvariantCulture);

                var spanStart = result.PageNumber;
                var spanEnd = result.PageNumber + Math.Max(0, result.PageSpan - 1);
                tableArtifact.Metadata[MetadataKeys.TablePageStart] = spanStart.ToString(CultureInfo.InvariantCulture);

                if (result.PageSpan > 1)
                {
                    tableArtifact.Metadata[MetadataKeys.TablePageEnd] = spanEnd.ToString(CultureInfo.InvariantCulture);
                }

                var range = spanStart == spanEnd
                    ? spanStart.ToString(CultureInfo.InvariantCulture)
                    : $"{spanStart.ToString(CultureInfo.InvariantCulture)}-{spanEnd.ToString(CultureInfo.InvariantCulture)}";
                tableArtifact.Metadata[MetadataKeys.TablePageRange] = range;

                artifacts.Tables.Add(tableArtifact);
            }

            EnsureContinuationPages(pageMap, result, tableLabel);
        }

        var segments = new List<DocumentSegment>();
        var imageNumber = 0;
        var orderedPages = pageMap.OrderBy(static entry => entry.Key).ToList();
        var totalPages = orderedPages.Count;
        var totalImages = orderedPages.Sum(entry => entry.Value.Images.Count);
        var processedPages = 0;
        var processedImages = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var pageStart = stopwatch.Elapsed;
        string FormatDuration(TimeSpan value)
        {
            var format = value.TotalHours >= 1 ? "hh\\:mm\\:ss'.'fff" : "mm\\:ss'.'fff";
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        foreach (var (originalPageNumber, accumulator) in orderedPages)
        {
            var markdown = accumulator.Markdown.ToString().TrimEnd();
            if (markdown.Length == 0)
            {
                markdown = $"<!-- Page {originalPageNumber} contained layout-only content -->";
            }

            var sequenceNumber = segments.Count + 1;
            var segment = CreatePageSegment(markdown, sequenceNumber, originalPageNumber, streamInfo.FileName);
            var index = segments.Count;
            segments.Add(segment);
            artifacts.TextBlocks.Add(new TextArtifact(markdown, originalPageNumber, streamInfo.FileName, segment.Label));

            processedPages++;
            var now = stopwatch.Elapsed;
            var pageDuration = now - pageStart;
            var totalElapsed = now;
            ReportDocxProgress(
                "docx-page",
                processedPages,
                Math.Max(totalPages, 1),
                $"page={originalPageNumber} duration={FormatDuration(pageDuration)} total={FormatDuration(totalElapsed)}");
            pageStart = now;

            foreach (var artifact in accumulator.Images)
            {
                artifact.SegmentIndex = index;
                artifact.Metadata[MetadataKeys.Page] = originalPageNumber.ToString(CultureInfo.InvariantCulture);
                artifact.Label ??= $"Image {++imageNumber}";
                artifacts.Images.Add(artifact);

                processedImages++;
                if (totalImages > 0)
                {
                    ReportDocxProgress("docx-images", processedImages, totalImages, $"page={originalPageNumber} label={artifact.Label}");
                }
            }
        }

        ValidatePageOrdering(segments);

        await EnrichImagesWithUnderstandingAsync(artifacts.Images, streamInfo, options, cancellationToken).ConfigureAwait(false);

        ReportDocxProgress("docx-completed", processedPages, Math.Max(totalPages, 1), $"pages={processedPages} images={processedImages} tables={tableCount} total={FormatDuration(stopwatch.Elapsed)}");

        var rawText = ComposeRawText(segments);

        return new DocxExtractionResult(segments, artifacts, rawText);
    }

    private static PageAccumulator GetOrCreatePageAccumulator(SortedDictionary<int, PageAccumulator> pageMap, int pageNumber)
    {
        if (!pageMap.TryGetValue(pageNumber, out var accumulator))
        {
            accumulator = new PageAccumulator();
            pageMap[pageNumber] = accumulator;
        }

        return accumulator;
    }

    private static void AppendMarkdown(StringBuilder builder, string markdown)
    {
        var normalized = markdown.TrimEnd();
        if (normalized.Length == 0)
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.AppendLine(normalized);
    }

    private static void EnsureContinuationPages(
        SortedDictionary<int, PageAccumulator> pageMap,
        ElementProcessingResult result,
        string? tableLabel)
    {
        if (result.PageSpan <= 1)
        {
            return;
        }

        for (var offset = 1; offset < result.PageSpan; offset++)
        {
            var pageNumber = result.PageNumber + offset;
            var continuation = GetOrCreatePageAccumulator(pageMap, pageNumber);
            var spanEnd = result.PageNumber + Math.Max(0, result.PageSpan - 1);

            string? placeholder = result.Kind switch
            {
                DocxElementKind.Table when tableLabel is not null
                    => $"<!-- {tableLabel} continues on page {pageNumber} (pages {result.PageNumber}-{spanEnd}) -->",
                DocxElementKind.Table
                    => $"<!-- Table continues on page {pageNumber} (pages {result.PageNumber}-{spanEnd}) -->",
                _ => $"<!-- Content continues from page {result.PageNumber} onto page {pageNumber} -->"
            };

            AppendMarkdown(continuation.Markdown, placeholder);
        }
    }

    private static string ComposeRawText(IEnumerable<DocumentSegment> segments)
    {
        var builder = new StringBuilder();

        foreach (var segment in segments)
        {
            if (segment?.Markdown is null)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine(segment.Markdown.TrimEnd());
        }

        return builder.ToString().Trim();
    }

    private static void ReportDocxProgress(string stage, int completed, int total, string? details)
    {
        var context = ConversionContextAccessor.Current;
        var progress = context?.Progress;
        if (progress is null)
        {
            return;
        }

        var detailLevel = context?.ProgressDetail ?? ProgressDetailLevel.Basic;
        if (detailLevel == ProgressDetailLevel.Basic &&
            (string.Equals(stage, "docx-pages", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(stage, "docx-images", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var normalizedTotal = Math.Max(total, 1);
        progress.Report(new ConversionProgress(stage, Math.Clamp(completed, 0, normalizedTotal), normalizedTotal, details));
    }

    private static void ValidatePageOrdering(IReadOnlyList<DocumentSegment> segments)
    {
        var expected = 1;

        foreach (var segment in segments)
        {
            if (segment?.Type != SegmentType.Page)
            {
                continue;
            }

            if (segment.Number != expected)
            {
                var actual = segment.Number.HasValue ? segment.Number.Value.ToString(CultureInfo.InvariantCulture) : "<null>";
                throw new InvalidOperationException($"Page segments are out of order. Expected page {expected} but found {actual}.");
            }

            expected++;
        }
    }

    private static DocumentSegment CreatePageSegment(string markdown, int sequenceNumber, int originalPageNumber, string? source)
    {
        var metadata = new Dictionary<string, string>
        {
            [MetadataKeys.Page] = originalPageNumber.ToString(CultureInfo.InvariantCulture)
        };

        return new DocumentSegment(
            markdown: markdown,
            type: SegmentType.Page,
            number: sequenceNumber,
            label: $"Page {sequenceNumber}",
            source: source,
            additionalMetadata: metadata);
    }


}
