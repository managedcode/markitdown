using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using ManagedCode.MimeTypes;
using MarkItDown;
using PDFtoImage;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for PDF files to Markdown using PdfPig.
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

        // Validate PDF header if we have access to the stream
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
                    var header = Encoding.ASCII.GetString(buffer);
                    return header == "%PDF";
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
            var workingStream = await EnsureSeekableStreamAsync(stream, cancellationToken).ConfigureAwait(false);
            try
            {
                workingStream.Position = 0;
                var extractedText = await ExtractTextFromPdfAsync(workingStream, cancellationToken).ConfigureAwait(false);

                workingStream.Position = 0;
                var pageImages = await RenderPdfImagesAsync(workingStream, cancellationToken).ConfigureAwait(false);

                var markdown = AppendImages(ConvertTextToMarkdown(extractedText), pageImages);
                var title = ExtractTitle(extractedText);

                return new DocumentConverterResult(markdown, title);
            }
            finally
            {
                if (!ReferenceEquals(workingStream, stream))
                {
                    await workingStream.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
        {
            throw new FileConversionException($"Failed to convert PDF file: {ex.Message}", ex);
        }
    }

    private static async Task<Stream> EnsureSeekableStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            return stream;
        }

        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;
        return buffer;
    }

    private static async Task<string> ExtractTextFromPdfAsync(Stream stream, CancellationToken cancellationToken)
    {
        var textBuilder = new StringBuilder();

        await Task.Run(() =>
        {
            using var pdfDocument = PdfDocument.Open(stream);

            for (int pageNum = 1; pageNum <= pdfDocument.NumberOfPages; pageNum++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = pdfDocument.GetPage(pageNum);
                var pageText = page.Text;

                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    if (textBuilder.Length > 0)
                    {
                        textBuilder.AppendLine("\n---\n"); // Page separator
                    }
                    textBuilder.AppendLine(pageText.Trim());
                }
            }
        }, cancellationToken);

        return textBuilder.ToString();
    }

    private static async Task<IReadOnlyList<string>> RenderPdfImagesAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (!(OperatingSystem.IsWindows()
            || OperatingSystem.IsLinux()
            || OperatingSystem.IsMacOS()
            || OperatingSystem.IsMacCatalyst()
            || OperatingSystem.IsAndroid()
            || OperatingSystem.IsIOS()))
        {
            return Array.Empty<string>();
        }

        return await RenderPdfImagesOnSupportedPlatformsAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("maccatalyst")]
    [SupportedOSPlatform("android")]
    [SupportedOSPlatform("ios")]
    private static Task<IReadOnlyList<string>> RenderPdfImagesOnSupportedPlatformsAsync(Stream stream, CancellationToken cancellationToken)
    {
        var options = new RenderOptions
        {
            Dpi = 144,
            WithAnnotations = true,
            WithAspectRatio = true,
            AntiAliasing = PdfAntiAliasing.All,
        };

        return Task.Run(() =>
        {
            stream.Position = 0;
            var images = new List<string>();

#pragma warning disable CA1416 // PDFtoImage is guarded by runtime platform checks above
            foreach (var bitmap in Conversion.ToImages(stream, leaveOpen: true, password: null, options))
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

            stream.Position = 0;
            return (IReadOnlyList<string>)images;
        }, cancellationToken);
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

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                result.AppendLine();
                continue;
            }

            // Handle page separators
            if (trimmedLine == "---")
            {
                result.AppendLine("\n---\n");
                continue;
            }

            // Detect potential headers (lines that are all caps and short)
            if (IsLikelyHeader(trimmedLine))
            {
                result.AppendLine($"## {trimmedLine}");
                result.AppendLine();
                continue;
            }

            // Handle bullet points and numbered lists
            if (IsListItem(trimmedLine))
            {
                result.AppendLine(ConvertListItem(trimmedLine));
                continue;
            }

            // Regular paragraph
            result.AppendLine(trimmedLine);
        }

        return result.ToString().Trim();
    }

    private static bool IsLikelyHeader(string line)
    {
        // Consider a line a header if it's:
        // - Relatively short (less than 80 characters)
        // - Has more uppercase than lowercase letters
        // - Doesn't end with punctuation that suggests it's not a title
        if (line.Length > 80 || line.EndsWith('.') || line.EndsWith(','))
            return false;

        var upperCount = line.Count(char.IsUpper);
        var lowerCount = line.Count(char.IsLower);
        
        return upperCount > lowerCount && upperCount > 2;
    }

    private static bool IsListItem(string line)
    {
        // Check for bullet points
        if (line.StartsWith("•") || line.StartsWith("-") || line.StartsWith("*"))
            return true;

        // Check for numbered lists
        var match = System.Text.RegularExpressions.Regex.Match(line, @"^\d+\.?\s+");
        return match.Success;
    }

    private static string ConvertListItem(string line)
    {
        // Convert bullet points
        if (line.StartsWith("•"))
            return line.Replace("•", "-");

        // Convert numbered items
        var match = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+)\.?\s+(.*)");
        if (match.Success)
        {
            return $"{match.Groups[1].Value}. {match.Groups[2].Value}";
        }

        return line;
    }

    private static string? ExtractTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        // Look for the first substantial line that could be a title
        foreach (var line in lines.Take(10)) // Check first 10 lines only
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
}
