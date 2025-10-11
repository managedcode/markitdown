using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using PDFtoImage;
using static PDFtoImage.Conversion;
using SkiaSharp;
using UglyToad.PdfPig;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for PDF files to Markdown using PdfPig for text extraction and optional page image rendering.
/// </summary>
public sealed class PdfConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    private static readonly HashSet<string> AcceptedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        MimeHelper.PDF
    };

    private readonly IPdfTextExtractor textExtractor;
    private readonly IPdfImageRenderer imageRenderer;
    private readonly SegmentOptions segmentOptions;
    private readonly IDocumentIntelligenceProvider? documentIntelligenceProvider;
    private readonly IImageUnderstandingProvider? imageUnderstandingProvider;
    private readonly IConversionPipeline conversionPipeline;

    public PdfConverter(
        SegmentOptions? segmentOptions = null,
        IDocumentIntelligenceProvider? documentProvider = null,
        IImageUnderstandingProvider? imageProvider = null,
        IConversionPipeline? pipeline = null)
        : this(new PdfPigTextExtractor(), new PdfToImageRenderer(), segmentOptions, documentProvider, imageProvider, pipeline)
    {
    }

    internal PdfConverter(
        IPdfTextExtractor textExtractor,
        IPdfImageRenderer imageRenderer,
        SegmentOptions? segmentOptions = null,
        IDocumentIntelligenceProvider? documentProvider = null,
        IImageUnderstandingProvider? imageProvider = null,
        IConversionPipeline? pipeline = null)
    {
        this.textExtractor = textExtractor ?? throw new ArgumentNullException(nameof(textExtractor));
        this.imageRenderer = imageRenderer ?? throw new ArgumentNullException(nameof(imageRenderer));
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
        documentIntelligenceProvider = documentProvider;
        imageUnderstandingProvider = imageProvider;
        conversionPipeline = pipeline ?? ConversionPipeline.Empty;
    }

    public int Priority => 200; // Between HTML and plain text

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = MimeTypeUtilities.NormalizeMime(streamInfo);
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && AcceptedExtensions.Contains(extension))
        {
            return true;
        }

        if (normalizedMime is not null && AcceptedMimeTypes.Contains(normalizedMime))
        {
            return true;
        }

        return streamInfo.MimeType is not null && AcceptedMimeTypes.Contains(streamInfo.MimeType);
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
                    return buffer[0] == '%' && buffer[1] == 'P' && buffer[2] == 'D' && buffer[3] == 'F';
                }
            }
            catch
            {
                stream.Position = originalPosition;
            }
        }

        return true;
    }

    private sealed class PdfExtractionResult
    {
        public PdfExtractionResult(List<DocumentSegment> segments, ConversionArtifacts artifacts, string rawText)
        {
            Segments = segments;
            Artifacts = artifacts;
            RawText = rawText;
        }

        public List<DocumentSegment> Segments { get; }

        public ConversionArtifacts Artifacts { get; }

        public string RawText { get; }
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            var pdfBytes = await ReadAllBytesAsync(stream, cancellationToken).ConfigureAwait(false);

            var extraction = await TryBuildExtractionFromDocumentIntelligenceAsync(pdfBytes, streamInfo, cancellationToken).ConfigureAwait(false)
                            ?? await BuildExtractionFromPdfPigAsync(pdfBytes, streamInfo, cancellationToken).ConfigureAwait(false);

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
            throw new FileConversionException($"Failed to convert PDF file: {ex.Message}", ex);
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream is MemoryStream existingMemory && stream.CanSeek)
        {
            stream.Position = 0;
            return existingMemory.ToArray();
        }

        await using var memory = new MemoryStream();
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    private async Task<PdfExtractionResult?> TryBuildExtractionFromDocumentIntelligenceAsync(byte[] pdfBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        if (documentIntelligenceProvider is null)
        {
            return null;
        }

        try
        {
            using var providerStream = new MemoryStream(pdfBytes, writable: false);
            var analysis = await documentIntelligenceProvider.AnalyzeAsync(providerStream, streamInfo, cancellationToken).ConfigureAwait(false);
            if (analysis is null || analysis.Pages.Count == 0)
            {
                return null;
            }

            return await BuildExtractionFromDocumentIntelligenceAsync(analysis, pdfBytes, streamInfo, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task<PdfExtractionResult> BuildExtractionFromDocumentIntelligenceAsync(DocumentIntelligenceResult analysis, byte[] pdfBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        var segments = new List<DocumentSegment>();
        var artifacts = new ConversionArtifacts();
        var rawTextBuilder = new StringBuilder();
        var processedTables = new HashSet<int>();
        var pagesWithInlineImages = new HashSet<int>();

        var orderedPages = analysis.Pages
            .OrderBy(p => p.PageNumber)
            .ToList();
        var distinctPageNumbers = orderedPages
            .Select(p => p.PageNumber)
            .Distinct()
            .ToList();

        foreach (var page in orderedPages)
        {
            if (!string.IsNullOrWhiteSpace(page.Text))
            {
                var text = page.Text.Trim();
                var metadata = new Dictionary<string, string>(page.Metadata)
                {
                    [MetadataKeys.Page] = page.PageNumber.ToString(CultureInfo.InvariantCulture)
                };

                var segment = new DocumentSegment(
                    markdown: text,
                    type: SegmentType.Page,
                    number: page.PageNumber,
                    label: $"Page {page.PageNumber}",
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

            foreach (var tableIndex in page.TableIndices.Distinct())
            {
                if (tableIndex < 0 || tableIndex >= analysis.Tables.Count)
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
                    [MetadataKeys.Page] = page.PageNumber.ToString(CultureInfo.InvariantCulture)
                };

                segments.Add(new DocumentSegment(
                    markdown: tableMarkdown,
                    type: SegmentType.Table,
                    number: tableIndex + 1,
                    label: $"Table {tableIndex + 1} (Page {page.PageNumber})",
                    source: streamInfo.FileName,
                    additionalMetadata: tableMetadata));

                if (processedTables.Add(tableIndex))
                {
                    var rows = table.Rows.Select(static row => (IList<string>)row.ToList()).ToList();
                    artifacts.Tables.Add(new TableArtifact(rows, page.PageNumber, streamInfo.FileName, $"Table {tableIndex + 1}"));
                }
            }

            foreach (var image in analysis.Images.Where(img => img.PageNumber == page.PageNumber))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var artifact = await CreateImageArtifactAsync(image, streamInfo, cancellationToken).ConfigureAwait(false);
                var base64 = Convert.ToBase64String(image.Content);
                var caption = artifact.Metadata.TryGetValue(MetadataKeys.Caption, out var storedCaption) ? storedCaption : artifact.Label;
                var markdown = $"![{caption ?? "Document image"}](data:{image.ContentType};base64,{base64})";
                artifact.PlaceholderMarkdown = markdown;

                var metadata = new Dictionary<string, string>(image.Metadata)
                {
                    [MetadataKeys.Page] = image.PageNumber.ToString(CultureInfo.InvariantCulture)
                };

                if (!string.IsNullOrWhiteSpace(caption))
                {
                    metadata[MetadataKeys.Caption] = caption!;
                }

                var imageSegment = new DocumentSegment(
                    markdown: markdown,
                    type: SegmentType.Image,
                    number: image.PageNumber,
                    label: caption ?? $"Image on page {image.PageNumber}",
                    source: streamInfo.FileName,
                    additionalMetadata: metadata);

                artifact.SegmentIndex = segments.Count;
                segments.Add(imageSegment);
                artifacts.Images.Add(artifact);
                pagesWithInlineImages.Add(page.PageNumber);
            }
        }

        await AppendMissingPageSnapshotsAsync(
            distinctPageNumbers,
            pagesWithInlineImages,
            pdfBytes,
            streamInfo,
            segments,
            artifacts,
            cancellationToken).ConfigureAwait(false);

        return new PdfExtractionResult(segments, artifacts, rawTextBuilder.ToString().Trim());
    }

    private PdfExtractionResult BuildExtractionFromExtractedText(IReadOnlyList<PdfPageText> pages, IReadOnlyList<string> pageImages, StreamInfo streamInfo)
    {
        var segments = new List<DocumentSegment>();
        var artifacts = new ConversionArtifacts();
        var rawTextBuilder = new StringBuilder();

        foreach (var page in pages)
        {
            var markdown = ConvertTextToMarkdown(page.Text);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                continue;
            }

            var metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Page] = page.PageNumber.ToString(CultureInfo.InvariantCulture)
            };

            var segment = new DocumentSegment(
                markdown: markdown,
                type: SegmentType.Page,
                number: page.PageNumber,
                label: $"Page {page.PageNumber}",
                source: streamInfo.FileName,
                additionalMetadata: metadata);

            segments.Add(segment);
            artifacts.TextBlocks.Add(new TextArtifact(markdown, page.PageNumber, streamInfo.FileName, segment.Label));

            if (rawTextBuilder.Length > 0)
            {
                rawTextBuilder.AppendLine();
            }

            rawTextBuilder.AppendLine(markdown);
        }

        if (pageImages.Count > 0)
        {
            segments.Add(new DocumentSegment(
                markdown: "## Page Images",
                type: SegmentType.Section,
                label: "Page Images",
                source: streamInfo.FileName));

            for (var i = 0; i < pageImages.Count; i++)
            {
                var markdown = $"![PDF page {i + 1}](data:image/png;base64,{pageImages[i]})";
                var metadata = new Dictionary<string, string>
                {
                    [MetadataKeys.Page] = (i + 1).ToString(CultureInfo.InvariantCulture)
                };

                var segmentIndex = segments.Count;
                segments.Add(new DocumentSegment(
                    markdown: markdown,
                    type: SegmentType.Image,
                    number: i + 1,
                    label: $"Page {i + 1} Image",
                    source: streamInfo.FileName,
                    additionalMetadata: metadata));

                var imageBytes = Convert.FromBase64String(pageImages[i]);
                var artifact = new ImageArtifact(imageBytes, "image/png", i + 1, streamInfo.FileName, $"Page {i + 1} Image")
                {
                    SegmentIndex = segmentIndex,
                    PlaceholderMarkdown = markdown
                };
                artifact.Metadata[MetadataKeys.Page] = (i + 1).ToString(CultureInfo.InvariantCulture);
                artifacts.Images.Add(artifact);
            }
        }

        return new PdfExtractionResult(segments, artifacts, rawTextBuilder.ToString().Trim());
    }

    private async Task<PdfExtractionResult> BuildExtractionFromPdfPigAsync(byte[] pdfBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        var pages = await textExtractor.ExtractTextAsync(pdfBytes, cancellationToken).ConfigureAwait(false);
        var pageImages = await imageRenderer.RenderImagesAsync(pdfBytes, cancellationToken).ConfigureAwait(false);
        return BuildExtractionFromExtractedText(pages, pageImages, streamInfo);
    }

    private async Task AppendMissingPageSnapshotsAsync(
        IReadOnlyList<int> pageNumbers,
        HashSet<int> pagesWithInlineImages,
        byte[] pdfBytes,
        StreamInfo streamInfo,
        List<DocumentSegment> segments,
        ConversionArtifacts artifacts,
        CancellationToken cancellationToken)
    {
        if (pageNumbers.Count == 0)
        {
            return;
        }

        var missingPages = pageNumbers
            .Where(page => !pagesWithInlineImages.Contains(page))
            .Distinct()
            .OrderBy(page => page)
            .ToList();

        if (missingPages.Count == 0)
        {
            return;
        }

        IReadOnlyList<string> renderedPages;

        try
        {
            renderedPages = await imageRenderer.RenderImagesAsync(pdfBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Rendering support is optional for document intelligence; ignore failures
            // so that conversions can still succeed when the renderer is unavailable.
            return;
        }

        if (renderedPages.Count == 0)
        {
            return;
        }

        var sectionAdded = false;

        foreach (var page in missingPages)
        {
            if (page <= 0 || page > renderedPages.Count)
            {
                continue;
            }

            var base64 = renderedPages[page - 1];
            if (string.IsNullOrWhiteSpace(base64))
            {
                continue;
            }

            byte[] imageBytes;
            try
            {
                imageBytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                continue;
            }

            if (!sectionAdded)
            {
                segments.Add(new DocumentSegment(
                    markdown: "## Page Snapshots",
                    type: SegmentType.Section,
                    label: "Page Snapshots",
                    source: streamInfo.FileName));
                sectionAdded = true;
            }

            var metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Page] = page.ToString(CultureInfo.InvariantCulture),
                ["snapshot"] = "true",
            };

            var segmentIndex = segments.Count;
            var placeholder = $"![PDF page {page}](data:image/png;base64,{base64})";
            segments.Add(new DocumentSegment(
                markdown: placeholder,
                type: SegmentType.Image,
                number: page,
                label: $"Page {page} Snapshot",
                source: streamInfo.FileName,
                additionalMetadata: metadata));

            var artifact = new ImageArtifact(imageBytes, "image/png", page, streamInfo.FileName, $"Page {page} Snapshot")
            {
                SegmentIndex = segmentIndex,
                PlaceholderMarkdown = placeholder
            };
            artifact.Metadata[MetadataKeys.Page] = page.ToString(CultureInfo.InvariantCulture);
            artifact.Metadata["snapshot"] = "true";
            artifacts.Images.Add(artifact);
        }
    }

    private async Task<ImageArtifact> CreateImageArtifactAsync(DocumentImageResult image, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        var artifact = new ImageArtifact(image.Content, image.ContentType, image.PageNumber, streamInfo.FileName)
        {
            Label = image.Caption ?? $"Image on page {image.PageNumber}"
        };

        artifact.Metadata[MetadataKeys.Page] = image.PageNumber.ToString(CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(image.Caption))
        {
            artifact.Metadata[MetadataKeys.Caption] = image.Caption!;
        }

        foreach (var pair in image.Metadata)
        {
            artifact.Metadata[pair.Key] = pair.Value;
        }

        if (image.Metadata.TryGetValue("ocrText", out var ocr) && !string.IsNullOrWhiteSpace(ocr))
        {
            artifact.RawText = ocr;
        }

        if (imageUnderstandingProvider is not null)
        {
            try
            {
                using var analysisStream = new MemoryStream(image.Content, writable: false);
                var result = await imageUnderstandingProvider.AnalyzeAsync(analysisStream, streamInfo, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(result?.Caption))
                {
                    artifact.Metadata[MetadataKeys.Caption] = result!.Caption!;
                    artifact.Label = result.Caption;
                }

                if (!string.IsNullOrWhiteSpace(result?.Text))
                {
                    artifact.Metadata["ocrText"] = result!.Text!;
                    artifact.RawText = result.Text;
                }
            }
            catch
            {
                // Ignore analysis failures; downstream middleware may handle enrichment.
            }
        }

        return artifact;
    }

    private static string ConvertTableToMarkdown(DocumentTableResult table)
    {
        if (table.Rows.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var header = table.Rows[0];
        builder.Append("| ");
        foreach (var cell in header)
        {
            builder.Append(EscapeMarkdownTableCell(cell)).Append(" | ");
        }
        builder.AppendLine();

        builder.Append("| ");
        foreach (var _ in header)
        {
            builder.Append("--- | ");
        }
        builder.AppendLine();

        for (var rowIndex = 1; rowIndex < table.Rows.Count; rowIndex++)
        {
            builder.Append("| ");
            foreach (var cell in table.Rows[rowIndex])
            {
                builder.Append(EscapeMarkdownTableCell(cell)).Append(" | ");
            }
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string EscapeMarkdownTableCell(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ").Trim();
    }

    private static string ConvertTextToMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var lines = text.Split('\n', StringSplitOptions.None);
        var result = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                result.AppendLine();
                continue;
            }

            if (trimmedLine == "---")
            {
                result.AppendLine("\n---\n");
                continue;
            }

            if (IsLikelyHeader(trimmedLine))
            {
                result.AppendLine($"## {trimmedLine}");
                result.AppendLine();
                continue;
            }

            if (IsListItem(trimmedLine))
            {
                result.AppendLine(ConvertListItem(trimmedLine));
                continue;
            }

            result.AppendLine(trimmedLine);
        }

        return result.ToString().Trim();
    }

    private static bool IsLikelyHeader(string line)
    {
        if (line.Length > 80 || line.EndsWith('.') || line.EndsWith(','))
            return false;

        var upperCount = line.Count(char.IsUpper);
        var lowerCount = line.Count(char.IsLower);

        return upperCount > lowerCount && upperCount > 2;
    }

    private static bool IsListItem(string line)
    {
        if (line.StartsWith("•") || line.StartsWith("-") || line.StartsWith("*"))
            return true;

        var match = System.Text.RegularExpressions.Regex.Match(line, @"^\d+\.?\s+");
        return match.Success;
    }

    private static string ConvertListItem(string line)
    {
        if (line.StartsWith("•"))
            return line.Replace("•", "-");

        var match = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+)\.?\s+(.*)");
        return match.Success ? $"{match.Groups[1].Value}. {match.Groups[2].Value}" : line;
    }

    private static string? ExtractTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines.Take(10))
        {
            var trimmedLine = line.Trim();

            if (trimmedLine.Length > 5 && trimmedLine.Length < 100 &&
                !trimmedLine.Contains("Page ") &&
                !trimmedLine.All(char.IsDigit))
            {
                return trimmedLine;
            }
        }

        return null;
    }

    internal interface IPdfTextExtractor
    {
        Task<IReadOnlyList<PdfPageText>> ExtractTextAsync(byte[] pdfBytes, CancellationToken cancellationToken);
    }

    internal interface IPdfImageRenderer
    {
        Task<IReadOnlyList<string>> RenderImagesAsync(byte[] pdfBytes, CancellationToken cancellationToken);
    }

    internal sealed record PdfPageText(int PageNumber, string Text);

    private sealed class PdfPigTextExtractor : IPdfTextExtractor
    {
        public Task<IReadOnlyList<PdfPageText>> ExtractTextAsync(byte[] pdfBytes, CancellationToken cancellationToken)
        {
            var pages = new List<PdfPageText>();

            using var pdfDocument = PdfDocument.Open(pdfBytes);

            for (var pageNumber = 1; pageNumber <= pdfDocument.NumberOfPages; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = pdfDocument.GetPage(pageNumber);
                var pageText = page.Text;

                if (string.IsNullOrWhiteSpace(pageText))
                {
                    continue;
                }

                pages.Add(new PdfPageText(pageNumber, pageText));
            }

            return Task.FromResult<IReadOnlyList<PdfPageText>>(pages);
        }
    }

    private sealed class PdfToImageRenderer : IPdfImageRenderer
    {
        public Task<IReadOnlyList<string>> RenderImagesAsync(byte[] pdfBytes, CancellationToken cancellationToken)
        {
            if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ||
                  OperatingSystem.IsMacCatalyst() || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()))
            {
                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            }

            return RenderOnSupportedPlatformsAsync(pdfBytes, cancellationToken);
        }

        [SupportedOSPlatform("windows")]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        [SupportedOSPlatform("maccatalyst")]
        [SupportedOSPlatform("android")]
        [SupportedOSPlatform("ios")]
        private static Task<IReadOnlyList<string>> RenderOnSupportedPlatformsAsync(byte[] pdfBytes, CancellationToken cancellationToken)
        {
            var images = new List<string>();
            var options = new RenderOptions
            {
                Dpi = 144,
                WithAnnotations = true,
                WithAspectRatio = true,
                AntiAliasing = PdfAntiAliasing.All,
            };

#pragma warning disable CA1416
            foreach (var bitmap in ToImages(pdfBytes, password: null, options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var bmp = bitmap;
                using var data = bmp.Encode(SKEncodedImageFormat.Png, quality: 90);
                if (data is null)
                {
                    continue;
                }

                images.Add(Convert.ToBase64String(data.Span));
            }
#pragma warning restore CA1416

            return Task.FromResult<IReadOnlyList<string>>(images);
        }
    }
}
