using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;
using MarkItDown;
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

    public PdfConverter()
        : this(new PdfPigTextExtractor(), new PdfToImageRenderer())
    {
    }

    internal PdfConverter(IPdfTextExtractor textExtractor, IPdfImageRenderer imageRenderer)
    {
        this.textExtractor = textExtractor ?? throw new ArgumentNullException(nameof(textExtractor));
        this.imageRenderer = imageRenderer ?? throw new ArgumentNullException(nameof(imageRenderer));
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

            var extractedText = await textExtractor.ExtractTextAsync(pdfBytes, cancellationToken).ConfigureAwait(false);
            var pageImages = await imageRenderer.RenderImagesAsync(pdfBytes, cancellationToken).ConfigureAwait(false);

            var markdown = AppendImages(ConvertTextToMarkdown(extractedText), pageImages);
            var title = ExtractTitle(extractedText);

            return new DocumentConverterResult(markdown, title);
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
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

    private static string AppendImages(string markdown, IReadOnlyList<string> images)
    {
        if (images.Count == 0)
        {
            return markdown;
        }

        var builder = new StringBuilder(markdown.Length + images.Count * 64);

        if (!string.IsNullOrWhiteSpace(markdown))
        {
            builder.Append(markdown.TrimEnd());
            builder.AppendLine();
            builder.AppendLine();
        }

        builder.AppendLine("## Page Images");
        builder.AppendLine();

        for (var i = 0; i < images.Count; i++)
        {
            builder.Append("![PDF page ");
            builder.Append(i + 1);
            builder.Append("](data:image/png;base64,");
            builder.Append(images[i]);
            builder.Append(")");
            builder.AppendLine();
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    internal interface IPdfTextExtractor
    {
        Task<string> ExtractTextAsync(byte[] pdfBytes, CancellationToken cancellationToken);
    }

    internal interface IPdfImageRenderer
    {
        Task<IReadOnlyList<string>> RenderImagesAsync(byte[] pdfBytes, CancellationToken cancellationToken);
    }

    private sealed class PdfPigTextExtractor : IPdfTextExtractor
    {
        public Task<string> ExtractTextAsync(byte[] pdfBytes, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var builder = new StringBuilder();

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

                    if (builder.Length > 0)
                    {
                        builder.AppendLine("\n---\n");
                    }

                    builder.AppendLine(pageText.Trim());
                }

                return builder.ToString();
            }, cancellationToken);
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
            return Task.Run(() =>
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

                return (IReadOnlyList<string>)images;
            }, cancellationToken);
        }
    }
}
