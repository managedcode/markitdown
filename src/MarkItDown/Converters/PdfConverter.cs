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

    public PdfConverter(
        SegmentOptions? segmentOptions = null,
        IDocumentIntelligenceProvider? documentProvider = null,
        IImageUnderstandingProvider? imageProvider = null)
        : this(new PdfPigTextExtractor(), new PdfToImageRenderer(), segmentOptions, documentProvider, imageProvider)
    {
    }

    internal PdfConverter(
        IPdfTextExtractor textExtractor,
        IPdfImageRenderer imageRenderer,
        SegmentOptions? segmentOptions = null,
        IDocumentIntelligenceProvider? documentProvider = null,
        IImageUnderstandingProvider? imageProvider = null)
    {
        this.textExtractor = textExtractor ?? throw new ArgumentNullException(nameof(textExtractor));
        this.imageRenderer = imageRenderer ?? throw new ArgumentNullException(nameof(imageRenderer));
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
        documentIntelligenceProvider = documentProvider;
        imageUnderstandingProvider = imageProvider;
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

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            var pdfBytes = await ReadAllBytesAsync(stream, cancellationToken).ConfigureAwait(false);

            var analysisSegments = await TryBuildSegmentsFromDocumentIntelligenceAsync(pdfBytes, streamInfo, cancellationToken).ConfigureAwait(false);
            if (analysisSegments is not null && analysisSegments.Count > 0)
            {
                var markdownFromAnalysis = SegmentMarkdownComposer.Compose(analysisSegments, segmentOptions);
                var titleFromAnalysis = ExtractTitle(string.Join(Environment.NewLine, analysisSegments.Select(s => s.Markdown)));
                return new DocumentConverterResult(markdownFromAnalysis, titleFromAnalysis, analysisSegments);
            }

            var pages = await textExtractor.ExtractTextAsync(pdfBytes, cancellationToken).ConfigureAwait(false);
            var pageImages = await imageRenderer.RenderImagesAsync(pdfBytes, cancellationToken).ConfigureAwait(false);

            var segments = BuildSegmentsFromExtractedText(pages, pageImages, streamInfo.FileName);
            var markdown = SegmentMarkdownComposer.Compose(segments, segmentOptions);

            var rawTextBuilder = new StringBuilder();
            foreach (var page in pages)
            {
                if (!string.IsNullOrWhiteSpace(page.Text))
                {
                    if (rawTextBuilder.Length > 0)
                    {
                        rawTextBuilder.AppendLine();
                    }

                    rawTextBuilder.AppendLine(page.Text.Trim());
                }
            }

            var title = ExtractTitle(rawTextBuilder.ToString());

            return new DocumentConverterResult(markdown, title, segments);
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

    private async Task<IReadOnlyList<DocumentSegment>?> TryBuildSegmentsFromDocumentIntelligenceAsync(byte[] pdfBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
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

            return await BuildSegmentsFromDocumentIntelligenceAsync(analysis, streamInfo, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<DocumentSegment>> BuildSegmentsFromDocumentIntelligenceAsync(DocumentIntelligenceResult analysis, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        var segments = new List<DocumentSegment>();
        var tableMarkdown = new string[analysis.Tables.Count];

        for (var i = 0; i < analysis.Tables.Count; i++)
        {
            tableMarkdown[i] = ConvertTableToMarkdown(analysis.Tables[i]);
        }

        foreach (var page in analysis.Pages.OrderBy(p => p.PageNumber))
        {
            if (!string.IsNullOrWhiteSpace(page.Text))
            {
                var metadata = new Dictionary<string, string>(page.Metadata)
                {
                    [MetadataKeys.Page] = page.PageNumber.ToString(CultureInfo.InvariantCulture)
                };

                segments.Add(new DocumentSegment(
                    markdown: page.Text.Trim(),
                    type: SegmentType.Page,
                    number: page.PageNumber,
                    label: $"Page {page.PageNumber}",
                    source: streamInfo.FileName,
                    additionalMetadata: metadata));
            }

            foreach (var tableIndex in page.TableIndices.Distinct())
            {
                if (tableIndex >= 0 && tableIndex < tableMarkdown.Length && !string.IsNullOrWhiteSpace(tableMarkdown[tableIndex]))
                {
                    var metadata = new Dictionary<string, string>(analysis.Tables[tableIndex].Metadata)
                    {
                        [MetadataKeys.TableIndex] = (tableIndex + 1).ToString(CultureInfo.InvariantCulture),
                        [MetadataKeys.Page] = page.PageNumber.ToString(CultureInfo.InvariantCulture)
                    };

                    segments.Add(new DocumentSegment(
                        markdown: tableMarkdown[tableIndex],
                        type: SegmentType.Table,
                        number: tableIndex + 1,
                        label: $"Table {tableIndex + 1} (Page {page.PageNumber})",
                        source: streamInfo.FileName,
                        additionalMetadata: metadata));
                }
            }
        }

        if (analysis.Images.Count > 0)
        {
            foreach (var image in analysis.Images)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var caption = image.Caption;
                if (caption is null && imageUnderstandingProvider is not null)
                {
                    using var imageStream = new MemoryStream(image.Content, writable: false);
                    var result = await imageUnderstandingProvider.AnalyzeAsync(imageStream, streamInfo, cancellationToken).ConfigureAwait(false);
                    caption = result?.Caption ?? result?.Text;
                }

                var base64 = Convert.ToBase64String(image.Content);
                var md = $"![{caption ?? "Document image"}](data:{image.ContentType};base64,{base64})";

                var metadata = new Dictionary<string, string>(image.Metadata)
                {
                    [MetadataKeys.Page] = image.PageNumber.ToString(CultureInfo.InvariantCulture)
                };

                if (!string.IsNullOrWhiteSpace(caption))
                {
                    metadata[MetadataKeys.Caption] = caption;
                }

                segments.Add(new DocumentSegment(
                    markdown: md,
                    type: SegmentType.Image,
                    number: image.PageNumber,
                    label: caption ?? $"Image on page {image.PageNumber}",
                    source: streamInfo.FileName,
                    additionalMetadata: metadata));
            }
        }

        return segments;
    }

    private static IReadOnlyList<DocumentSegment> BuildSegmentsFromExtractedText(
        IReadOnlyList<PdfPageText> pages,
        IReadOnlyList<string> pageImages,
        string? source)
    {
        var segments = new List<DocumentSegment>();

        foreach (var page in pages)
        {
            var markdown = ConvertTextToMarkdown(page.Text);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                continue;
            }

            var metadata = new Dictionary<string, string>
            {
                ["page"] = page.PageNumber.ToString(CultureInfo.InvariantCulture)
            };

            segments.Add(new DocumentSegment(
                markdown: markdown,
                type: SegmentType.Page,
                number: page.PageNumber,
                label: $"Page {page.PageNumber}",
                source: source,
                additionalMetadata: metadata));
        }

        if (pageImages.Count > 0)
        {
            segments.Add(new DocumentSegment(
                markdown: "## Page Images",
                type: SegmentType.Section,
                label: "Page Images",
                source: source));

            for (var i = 0; i < pageImages.Count; i++)
            {
                var markdown = $"![PDF page {i + 1}](data:image/png;base64,{pageImages[i]})";
                var metadata = new Dictionary<string, string>
                {
                    ["page"] = (i + 1).ToString(CultureInfo.InvariantCulture)
                };

                segments.Add(new DocumentSegment(
                    markdown: markdown,
                    type: SegmentType.Image,
                    number: i + 1,
                    label: $"Page {i + 1} Image",
                    source: source,
                    additionalMetadata: metadata));
            }
        }

        return segments;
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
            foreach (var bitmap in Conversion.ToImages(pdfBytes, password: null, options))
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
