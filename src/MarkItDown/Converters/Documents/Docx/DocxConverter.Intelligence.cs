using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Wordprocessing;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

public sealed partial class DocxConverter
{
    private async Task<DocumentIntelligenceResult?> AnalyzeWithDocumentIntelligenceAsync(string docxPath, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        var context = ConversionContextAccessor.Current;
        var provider = context?.Providers.Document ?? documentIntelligenceProvider;
        var request = context?.Request.Intelligence.Document;

        if (provider is null)
        {
            return null;
        }

        try
        {
            using var providerStream = OpenReadOnlyFile(docxPath);
            return await provider.AnalyzeAsync(providerStream, streamInfo, request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task<DocxExtractionResult> BuildExtractionFromDocumentIntelligenceAsync(
        DocumentIntelligenceResult analysis,
        DocxExtractionResult localExtraction,
        StreamInfo streamInfo,
        CancellationToken cancellationToken)
    {
        var segments = new List<DocumentSegment>();
        var artifacts = new ConversionArtifacts();
        var rawTextBuilder = new StringBuilder();
        var processedTables = new HashSet<int>();

        foreach (var kvp in localExtraction.Artifacts.Metadata)
        {
            artifacts.Metadata[kvp.Key] = kvp.Value;
        }

        var imagesByPage = new Dictionary<int, List<ImageArtifact>>();

        foreach (var image in localExtraction.Artifacts.Images)
        {
            var pageKey = GetImagePageNumber(image);
            if (pageKey.HasValue)
            {
                if (!imagesByPage.TryGetValue(pageKey.Value, out var list))
                {
                    list = new List<ImageArtifact>();
                    imagesByPage[pageKey.Value] = list;
                }

                list.Add(image);
            }
            else
            {
                // Images without an associated page will be appended after page-level processing.
            }
        }

        var orderedPages = analysis.Pages
            .OrderBy(page => page.PageNumber)
            .ToList();

        foreach (var page in orderedPages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(page.Text))
            {
                var text = page.Text.Trim();
                var metadata = new Dictionary<string, string>(page.Metadata)
                {
                    [MetadataKeys.Page] = page.PageNumber.ToString(CultureInfo.InvariantCulture)
                };

                var sequenceNumber = segments.Count + 1;
                var segment = new DocumentSegment(
                    markdown: text,
                    type: SegmentType.Page,
                    number: sequenceNumber,
                    label: $"Page {sequenceNumber}",
                    source: streamInfo.FileName,
                    additionalMetadata: metadata);

                segments.Add(segment);
                artifacts.TextBlocks.Add(new TextArtifact(text, page.PageNumber, streamInfo.FileName, segment.Label));

                if (rawTextBuilder.Length > 0)
                {
                    rawTextBuilder.AppendLine();
                }

                rawTextBuilder.AppendLine(text);
            }

            if (imagesByPage.TryGetValue(page.PageNumber, out var pageImages))
            {
                foreach (var image in pageImages)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var placeholder = EnsureImagePlaceholder(image);
                    var imageMetadata = new Dictionary<string, string>(image.Metadata)
                    {
                        [MetadataKeys.Page] = page.PageNumber.ToString(CultureInfo.InvariantCulture)
                    };

                    var label = image.Label ?? $"Image on page {page.PageNumber}";

                    var imageSegment = new DocumentSegment(
                        markdown: placeholder,
                        type: SegmentType.Image,
                        number: page.PageNumber,
                        label: label,
                        source: streamInfo.FileName,
                        additionalMetadata: imageMetadata);

                    image.SegmentIndex = segments.Count;
                    image.Metadata[MetadataKeys.Page] = page.PageNumber.ToString(CultureInfo.InvariantCulture);
                    segments.Add(imageSegment);
                    artifacts.Images.Add(image);
                }
            }

            foreach (var tableIndex in page.TableIndices.Distinct())
            {
                if (tableIndex < 0 || tableIndex >= analysis.Tables.Count)
                {
                    continue;
                }

                if (!processedTables.Add(tableIndex))
                {
                    continue;
                }

                var table = analysis.Tables[tableIndex];
                var tableMarkdown = ConvertTableToMarkdown(table);
                if (string.IsNullOrWhiteSpace(tableMarkdown))
                {
                    continue;
                }

                var tableMetadata = new Dictionary<string, string>(table.Metadata)
                {
                    [MetadataKeys.TableIndex] = (tableIndex + 1).ToString(CultureInfo.InvariantCulture),
                    [MetadataKeys.Page] = table.PageNumber.ToString(CultureInfo.InvariantCulture)
                };

                var tableSegment = new DocumentSegment(
                    markdown: tableMarkdown,
                    type: SegmentType.Table,
                    number: tableIndex + 1,
                    label: $"Table {tableIndex + 1} (Page {table.PageNumber})",
                    source: streamInfo.FileName,
                    additionalMetadata: tableMetadata);

                segments.Add(tableSegment);

                var rows = table.Rows
                    .Select(static row => (IList<string>)row.ToList())
                    .ToList();

                artifacts.Tables.Add(new TableArtifact(rows, table.PageNumber, streamInfo.FileName, $"Table {tableIndex + 1}"));
            }
        }

        // Append any images that did not have an associated page in the intelligence output.
        foreach (var image in localExtraction.Artifacts.Images)
        {
            if (artifacts.Images.Contains(image))
            {
                continue;
            }

            var placeholder = EnsureImagePlaceholder(image);
            var pageNumber = GetImagePageNumber(image);
            var metadata = new Dictionary<string, string>(image.Metadata);
            if (pageNumber.HasValue)
            {
                metadata[MetadataKeys.Page] = pageNumber.Value.ToString(CultureInfo.InvariantCulture);
            }

            var imageSegment = new DocumentSegment(
                markdown: placeholder,
                type: SegmentType.Image,
                number: pageNumber,
                label: image.Label ?? (pageNumber.HasValue ? $"Image on page {pageNumber}" : "Document image"),
                source: streamInfo.FileName,
                additionalMetadata: metadata);

            image.SegmentIndex = segments.Count;
            segments.Add(imageSegment);
            artifacts.Images.Add(image);
        }

        if (segments.All(segment => segment.Type == SegmentType.Image))
        {
            return localExtraction;
        }

        artifacts.Metadata[MetadataKeys.DocumentIntelligenceProvider] = MetadataValues.ProviderAzureDocumentIntelligence;

        var rawText = rawTextBuilder.Length > 0
            ? rawTextBuilder.ToString().Trim()
            : localExtraction.RawText;

        ValidatePageOrdering(segments);

        var options = ResolveSegmentOptions();
        await EnrichImagesWithUnderstandingAsync(artifacts.Images, streamInfo, options, cancellationToken).ConfigureAwait(false);

        return new DocxExtractionResult(segments, artifacts, rawText);
    }

    private async Task EnrichImagesWithUnderstandingAsync(IList<ImageArtifact> images, StreamInfo streamInfo, SegmentOptions options, CancellationToken cancellationToken)
    {
        if (images.Count == 0)
        {
            return;
        }

        if (!options.Image.EnableImageUnderstandingProvider)
        {
            return;
        }

        var context = ConversionContextAccessor.Current;
        var provider = context?.Providers.Image ?? imageUnderstandingProvider;
        var request = context?.Request.Intelligence.Image;
        if (provider is null)
        {
            return;
        }

        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var buffer = OpenImageStream(image);
            var extension = GuessImageExtension(image.ContentType) ?? streamInfo.Extension;
            var imageInfo = new StreamInfo(
                mimeType: image.ContentType ?? streamInfo.MimeType,
                extension: extension,
                fileName: image.Source,
                localPath: streamInfo.LocalPath,
                url: streamInfo.Url);

            ImageUnderstandingResult? result;
            try
            {
                result = await provider.AnalyzeAsync(buffer, imageInfo, request, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

            if (result is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(result.Caption))
            {
                image.Metadata[MetadataKeys.Caption] = result.Caption!;
                image.Label ??= result.Caption;
            }

            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                image.Metadata[MetadataKeys.OcrText] = result.Text!;
                if (string.IsNullOrWhiteSpace(image.RawText))
                {
                    image.RawText = result.Text;
                }
            }

            if (result.Metadata.Count > 0)
            {
                foreach (var kvp in result.Metadata)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
                    {
                        continue;
                    }

                    image.Metadata[kvp.Key] = kvp.Value;
                }
            }
        }
    }

    private static FileStream OpenImageStream(ImageArtifact image)
    {
        if (!string.IsNullOrWhiteSpace(image.FilePath) && File.Exists(image.FilePath))
        {
            return OpenReadOnlyFile(image.FilePath);
        }

        throw new InvalidOperationException("Image artifact is missing a persisted file path.");
    }

    private static string? GuessImageExtension(string? mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
        {
            return null;
        }

        if (!MimeHelper.TryGetExtensions(mime, out var extensions) || extensions.Count == 0)
        {
            return null;
        }

        return extensions.FirstOrDefault();
    }
}
