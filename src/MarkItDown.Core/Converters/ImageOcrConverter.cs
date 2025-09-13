using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using System.Text;
using Tesseract;

namespace MarkItDown.Core.Converters;

/// <summary>
/// Converter for image files to Markdown using OCR (Optical Character Recognition).
/// Requires Tesseract OCR data files to be available.
/// </summary>
public sealed class ImageOcrConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp"
    };

    private static readonly HashSet<string> AcceptedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/gif", "image/bmp", 
        "image/tiff", "image/webp"
    };

    private readonly string? _tessDataPath;

    public int Priority => 500; // Lower priority since OCR is resource intensive

    /// <summary>
    /// Creates a new ImageOcrConverter.
    /// </summary>
    /// <param name="tessDataPath">Optional path to Tesseract data files. If null, uses default system path.</param>
    public ImageOcrConverter(string? tessDataPath = null)
    {
        _tessDataPath = tessDataPath;
    }

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var mimeType = streamInfo.MimeType?.ToLowerInvariant() ?? string.Empty;
        var extension = streamInfo.Extension?.ToLowerInvariant();

        // Check the extension
        if (extension is not null && AcceptedExtensions.Contains(extension))
            return true;

        // Check the mimetype
        if (AcceptedMimeTypes.Any(mt => mimeType.StartsWith(mt)))
            return true;

        return false;
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (!AcceptsInput(streamInfo))
            return false;

        // Try to validate if it's actually an image
        if (stream.CanSeek && stream.Length > 0)
        {
            var originalPosition = stream.Position;
            try
            {
                stream.Position = 0;
                
                // Try to detect image format using ImageSharp
                var format = Image.DetectFormat(stream);
                stream.Position = originalPosition;
                
                return format != null;
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
            // Reset stream position
            if (stream.CanSeek)
                stream.Position = 0;

            var extractedText = await ExtractTextFromImageAsync(stream, cancellationToken);
            var markdown = ConvertOcrTextToMarkdown(extractedText);
            var title = ExtractTitle(extractedText, streamInfo.FileName);

            return new DocumentConverterResult(markdown, title);
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
        {
            throw new FileConversionException($"Failed to convert image file: {ex.Message}", ex);
        }
    }

    private async Task<string> ExtractTextFromImageAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() =>
            {
                // Load image using ImageSharp
                using var image = Image.Load(stream);
                
                // Convert to byte array for Tesseract
                using var memoryStream = new MemoryStream();
                image.SaveAsPng(memoryStream);
                var imageBytes = memoryStream.ToArray();

                // Use Tesseract for OCR
                var tessDataPath = _tessDataPath ?? Environment.GetEnvironmentVariable("TESSDATA_PREFIX") ?? "/usr/share/tesseract-ocr/4.00/tessdata";
                
                using var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
                using var img = Pix.LoadFromMemory(imageBytes);
                using var page = engine.Process(img);
                
                var text = page.GetText();
                return text?.Trim() ?? "";
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // If OCR fails, provide a graceful fallback
            var fileName = Path.GetFileName(stream is FileStream fs ? fs.Name : "image");
            return $"*Image content could not be extracted via OCR*\n\nImage: {fileName}\n\nError: {ex.Message}";
        }
    }

    private static string ConvertOcrTextToMarkdown(string ocrText)
    {
        if (string.IsNullOrWhiteSpace(ocrText))
            return "*No text detected in image*";

        // If it's an error message, return as-is
        if (ocrText.Contains("*Image content could not be extracted"))
            return ocrText;

        var result = new StringBuilder();
        result.AppendLine("## Text extracted from image");
        result.AppendLine();

        var lines = ocrText.Split('\n', StringSplitOptions.None);
        var currentParagraph = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // Empty line indicates paragraph break
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                if (currentParagraph.Length > 0)
                {
                    result.AppendLine(currentParagraph.ToString().Trim());
                    result.AppendLine();
                    currentParagraph.Clear();
                }
                continue;
            }

            // Check if line looks like a header (short, could be all caps)
            if (IsLikelyHeader(trimmedLine))
            {
                // Finish current paragraph first
                if (currentParagraph.Length > 0)
                {
                    result.AppendLine(currentParagraph.ToString().Trim());
                    result.AppendLine();
                    currentParagraph.Clear();
                }
                
                result.AppendLine($"### {trimmedLine}");
                result.AppendLine();
                continue;
            }

            // Add to current paragraph
            if (currentParagraph.Length > 0)
                currentParagraph.Append(' ');
            currentParagraph.Append(trimmedLine);
        }

        // Don't forget the last paragraph
        if (currentParagraph.Length > 0)
        {
            result.AppendLine(currentParagraph.ToString().Trim());
        }

        return result.ToString().Trim();
    }

    private static bool IsLikelyHeader(string line)
    {
        // Consider a line a header if it's:
        // - Relatively short (less than 60 characters)
        // - Has more uppercase than lowercase letters OR is title case
        // - Doesn't end with punctuation that suggests it's not a title
        if (line.Length > 60 || line.EndsWith('.') || line.EndsWith(',') || line.EndsWith(';'))
            return false;

        var upperCount = line.Count(char.IsUpper);
        var lowerCount = line.Count(char.IsLower);
        var wordCount = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        
        // All caps and short
        if (upperCount > lowerCount && upperCount > 2 && wordCount <= 5)
            return true;

        // Title case detection (first letter of each word is uppercase)
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 5 && words.All(w => w.Length > 0 && char.IsUpper(w[0])))
            return true;

        return false;
    }

    private static string ExtractTitle(string ocrText, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(ocrText) && !ocrText.Contains("*Image content could not be extracted"))
        {
            var lines = ocrText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            // Look for the first substantial line that could be a title
            foreach (var line in lines.Take(5))
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.Length > 3 && trimmedLine.Length < 100)
                {
                    return trimmedLine;
                }
            }
        }

        // Fallback to filename
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            return string.IsNullOrWhiteSpace(nameWithoutExtension) ? "Image Document" : nameWithoutExtension;
        }

        return "Image Document";
    }
}