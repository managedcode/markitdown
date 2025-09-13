using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Drawing;
using System.Text;
using A = DocumentFormat.OpenXml.Drawing;

namespace MarkItDown.Core.Converters;

/// <summary>
/// Converter for Microsoft PowerPoint (.pptx) files to Markdown using DocumentFormat.OpenXml.
/// </summary>
public sealed class PptxConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pptx"
    };

    private static readonly HashSet<string> AcceptedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.presentationml.presentation"
    };

    public int Priority => 230; // Between XLSX and plain text

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var mimeType = streamInfo.MimeType?.ToLowerInvariant() ?? string.Empty;
        var extension = streamInfo.Extension?.ToLowerInvariant();

        // Check the extension
        if (extension is not null && AcceptedExtensions.Contains(extension))
            return true;

        // Check the mimetype
        if (AcceptedMimeTypes.Contains(mimeType))
            return true;

        return false;
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (!AcceptsInput(streamInfo))
            return false;

        // Validate ZIP/PPTX header if we have access to the stream
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
                    // Check for ZIP file signature (PPTX files are ZIP archives)
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
            // Reset stream position
            if (stream.CanSeek)
                stream.Position = 0;

            var markdown = await ExtractContentFromPptxAsync(stream, cancellationToken);
            var title = ExtractTitle(markdown, streamInfo.FileName ?? "PowerPoint Presentation");

            return new DocumentConverterResult(markdown, title);
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
        {
            throw new FileConversionException($"Failed to convert PPTX file: {ex.Message}", ex);
        }
    }

    private static async Task<string> ExtractContentFromPptxAsync(Stream stream, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();

        await Task.Run(() =>
        {
            using var presentationDocument = PresentationDocument.Open(stream, false);
            var presentationPart = presentationDocument.PresentationPart;
            
            if (presentationPart?.Presentation?.SlideIdList != null)
            {
                var slideCount = 0;
                
                foreach (var slideId in presentationPart.Presentation.SlideIdList.Elements<SlideId>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    slideCount++;
                    var slidePart = (SlidePart)presentationPart.GetPartById(slideId.RelationshipId!);
                    ProcessSlide(slidePart, slideCount, result);
                }
            }
        }, cancellationToken);

        return result.ToString().Trim();
    }

    private static void ProcessSlide(SlidePart slidePart, int slideNumber, StringBuilder result)
    {
        result.AppendLine($"## Slide {slideNumber}");
        result.AppendLine();

        var slide = slidePart.Slide;
        if (slide.CommonSlideData?.ShapeTree != null)
        {
            foreach (var shape in slide.CommonSlideData.ShapeTree.Elements())
            {
                if (shape is DocumentFormat.OpenXml.Presentation.Shape textShape)
                {
                    ProcessTextShape(textShape, result);
                }
            }
        }

        result.AppendLine();
    }

    private static void ProcessTextShape(DocumentFormat.OpenXml.Presentation.Shape shape, StringBuilder result)
    {
        var textBody = shape.TextBody;
        if (textBody == null)
            return;

        foreach (var paragraph in textBody.Elements<A.Paragraph>())
        {
            var paragraphText = new StringBuilder();
            var isTitle = false;
            
            // Check if this is a title based on placeholder type
            var placeholderShape = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape;
            if (placeholderShape?.Type?.Value == PlaceholderValues.Title || 
                placeholderShape?.Type?.Value == PlaceholderValues.CenteredTitle ||
                placeholderShape?.Type?.Value == PlaceholderValues.SubTitle)
            {
                isTitle = true;
            }

            // Process runs in the paragraph
            foreach (var run in paragraph.Elements<A.Run>())
            {
                var runProperties = run.RunProperties;
                var text = run.Text?.Text ?? "";
                
                if (!string.IsNullOrEmpty(text))
                {
                    // Apply formatting based on run properties
                    if (runProperties?.Bold?.Value == true)
                        text = $"**{text}**";
                    if (runProperties?.Italic?.Value == true)
                        text = $"*{text}*";
                        
                    paragraphText.Append(text);
                }
            }

            // Process text without runs (direct text)
            foreach (var text in paragraph.Elements<A.Text>())
            {
                paragraphText.Append(text.Text);
            }

            var finalText = paragraphText.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                if (isTitle)
                {
                    result.AppendLine($"### {finalText}");
                }
                else
                {
                    // For now, just output as regular text
                    // Bullet point detection in PowerPoint is complex and varies by version
                    result.AppendLine(finalText);
                }
                result.AppendLine();
            }
        }
    }

    private static string ExtractTitle(string markdown, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(markdown))
        {
            var lines = markdown.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            // Look for the first heading (### from slide title)
            foreach (var line in lines.Take(10))
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("###"))
                {
                    return trimmedLine.TrimStart('#').Trim();
                }
            }

            // If no heading found, use the first substantial line
            foreach (var line in lines.Take(5))
            {
                var trimmedLine = line.Trim();
                if (!trimmedLine.StartsWith("##") && trimmedLine.Length > 5 && trimmedLine.Length < 100)
                {
                    return trimmedLine;
                }
            }
        }

        // Fallback to filename
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var nameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(fileName);
            return string.IsNullOrWhiteSpace(nameWithoutExtension) ? "PowerPoint Presentation" : nameWithoutExtension;
        }

        return "PowerPoint Presentation";
    }
}