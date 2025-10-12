using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using ManagedCode.MimeTypes;
using MarkItDown.Intelligence;
using A = DocumentFormat.OpenXml.Drawing;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for Microsoft PowerPoint (.pptx) files to Markdown using DocumentFormat.OpenXml.
/// </summary>
public sealed class PptxConverter : DocumentConverterBase
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pptx"
    };

    private static readonly HashSet<string> AcceptedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        MimeHelper.GetMimeType(".pptx"),
    };

    private readonly SegmentOptions segmentOptions;
    private readonly IConversionPipeline conversionPipeline;
    private readonly IImageUnderstandingProvider? imageUnderstandingProvider;

    public PptxConverter(SegmentOptions? segmentOptions = null, IConversionPipeline? pipeline = null, IImageUnderstandingProvider? imageProvider = null)
        : base(priority: 230) // Between XLSX and plain text
    {
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
        conversionPipeline = pipeline ?? ConversionPipeline.Empty;
        imageUnderstandingProvider = this.segmentOptions.Image.EnableImageUnderstandingProvider ? imageProvider : null;
    }

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = streamInfo.ResolveMimeType();
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

    public override bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
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
                Span<byte> buffer = stackalloc byte[4];
                var bytesRead = stream.Read(buffer);
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

    public override async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            var extraction = await ExtractSlidesAsync(stream, streamInfo, cancellationToken).ConfigureAwait(false);
            await conversionPipeline.ExecuteAsync(streamInfo, extraction.Artifacts, extraction.Segments, cancellationToken).ConfigureAwait(false);

            var generatedAt = DateTime.UtcNow;
            var titleHint = ExtractTitle(extraction.RawText, streamInfo.FileName, string.Empty);
            var meta = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, streamInfo, segmentOptions, titleHint, generatedAt);
            var title = meta.Title
                ?? titleHint
                ?? ExtractTitle(extraction.RawText, streamInfo.FileName, meta.Markdown)
                ?? Path.GetFileNameWithoutExtension(streamInfo.FileName ?? streamInfo.LocalPath ?? streamInfo.Url)
                ?? "Presentation";

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataKeys.DocumentTitle] = title,
                [MetadataKeys.DocumentPages] = extraction.Segments.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentImages] = extraction.Artifacts.Images.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentTables] = extraction.Artifacts.Tables.Count.ToString(CultureInfo.InvariantCulture)
            };

            if (!string.IsNullOrWhiteSpace(titleHint))
            {
                metadata[MetadataKeys.DocumentTitleHint] = titleHint!;
            }

            string MarkdownFactory()
            {
                var recomposed = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, streamInfo, segmentOptions, titleHint, generatedAt);
                return recomposed.Markdown;
            }

            return DocumentConverterResult.FromFactory(
                MarkdownFactory,
                title,
                extraction.Segments,
                extraction.Artifacts,
                metadata,
                generatedAtUtc: generatedAt);
        }
        catch (Exception ex) when (ex is not MarkItDownException)
        {
            throw new FileConversionException($"Failed to convert PPTX file: {ex.Message}", ex);
        }
    }

    private sealed class PptxExtractionResult
    {
        public PptxExtractionResult(List<DocumentSegment> segments, ConversionArtifacts artifacts, string rawText)
        {
            Segments = segments;
            Artifacts = artifacts;
            RawText = rawText;
        }

        public List<DocumentSegment> Segments { get; }

        public ConversionArtifacts Artifacts { get; }

        public string RawText { get; }
    }

    private sealed record SlideExtractionResult(string Markdown, string Text, IReadOnlyList<ImageArtifact> Images);

    private async Task<PptxExtractionResult> ExtractSlidesAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        using var presentationDocument = PresentationDocument.Open(stream, false);
        var presentationPart = presentationDocument.PresentationPart;
        if (presentationPart?.Presentation?.SlideIdList is null)
        {
            return new PptxExtractionResult(new List<DocumentSegment>(), new ConversionArtifacts(), string.Empty);
        }

        var segments = new List<DocumentSegment>();
        var artifacts = new ConversionArtifacts();
        var rawTextBuilder = new StringBuilder();
        var slideIndex = 0;

        foreach (var slideId in presentationPart.Presentation.SlideIdList.Elements<SlideId>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            slideIndex++;

            if (presentationPart.GetPartById(slideId.RelationshipId!) is not SlidePart slidePart)
            {
                continue;
            }

            var slideResult = await ConvertSlideToMarkdownAsync(slidePart, slideIndex, streamInfo, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(slideResult.Markdown) && slideResult.Images.Count == 0)
            {
                continue;
            }

            var metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Slide] = slideIndex.ToString(CultureInfo.InvariantCulture)
            };

            var segment = new DocumentSegment(
                markdown: slideResult.Markdown,
                type: SegmentType.Slide,
                number: slideIndex,
                label: $"Slide {slideIndex}",
                source: streamInfo.FileName,
                additionalMetadata: metadata);

            var segmentIndex = segments.Count;
            segments.Add(segment);

            var textContent = string.IsNullOrWhiteSpace(slideResult.Text) ? slideResult.Markdown : slideResult.Text;
            artifacts.TextBlocks.Add(new TextArtifact(textContent, slideIndex, streamInfo.FileName, segment.Label));

            if (!string.IsNullOrWhiteSpace(textContent))
            {
                if (rawTextBuilder.Length > 0)
                {
                    rawTextBuilder.AppendLine();
                }

                rawTextBuilder.AppendLine(textContent);
            }

            foreach (var image in slideResult.Images)
            {
                image.SegmentIndex = segmentIndex;
                artifacts.Images.Add(image);
            }
        }

        return new PptxExtractionResult(segments, artifacts, rawTextBuilder.ToString().Trim());
    }

    private async Task<SlideExtractionResult> ConvertSlideToMarkdownAsync(SlidePart slidePart, int slideNumber, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        var markdown = new StringBuilder();
        var text = new StringBuilder();
        var images = new List<ImageArtifact>();
        var slide = slidePart.Slide;
        var slideImageIndex = 0;

        markdown.AppendLine($"## Slide {slideNumber}");
        markdown.AppendLine();
        text.AppendLine($"Slide {slideNumber}");

        if (slide.CommonSlideData?.ShapeTree is not null)
        {
            foreach (var element in slide.CommonSlideData.ShapeTree.Elements())
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (element)
                {
                    case DocumentFormat.OpenXml.Presentation.Shape textShape:
                        AppendTextShape(textShape, markdown, text);
                        break;
                    case DocumentFormat.OpenXml.Presentation.Picture picture:
                    {
                        var artifact = await ExtractImageAsync(picture, slidePart, slideNumber, streamInfo, cancellationToken).ConfigureAwait(false);
                        if (artifact is null)
                        {
                            break;
                        }

                        slideImageIndex++;
                        var label = artifact.Label ?? $"Slide {slideNumber} Image {slideImageIndex}";
                        artifact.Label = label;

                        var mimeType = string.IsNullOrWhiteSpace(artifact.ContentType)
                            ? MimeHelper.PNG
                            : artifact.ContentType;

                        var base64 = Convert.ToBase64String(artifact.Data);
                        var placeholder = $"![{label}](data:{mimeType};base64,{base64})";
                        artifact.PlaceholderMarkdown = placeholder;
                        markdown.AppendLine(placeholder);
                        markdown.AppendLine();

                        text.AppendLine(label);
                        images.Add(artifact);
                        break;
                    }
                }
            }
        }

        var markdownResult = markdown.ToString().TrimEnd();
        var textResult = text.ToString().Trim();
        return new SlideExtractionResult(markdownResult, textResult, images);
    }

    private async Task<ImageArtifact?> ExtractImageAsync(DocumentFormat.OpenXml.Presentation.Picture picture, SlidePart slidePart, int slideNumber, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        var blip = picture.BlipFill?.Blip;
        var relationshipId = blip?.Embed?.Value ?? blip?.Link?.Value;
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            return null;
        }

        if (slidePart.GetPartById(relationshipId) is not ImagePart imagePart)
        {
            return null;
        }

        await using var partStream = imagePart.GetStream();
        var extensionHint = Path.GetExtension(imagePart.Uri?.ToString() ?? string.Empty);
        await using var imageHandle = await DiskBufferHandle.FromStreamAsync(partStream, extensionHint, bufferSize: 128 * 1024, onChunkWritten: null, cancellationToken).ConfigureAwait(false);
        var data = await File.ReadAllBytesAsync(imageHandle.FilePath, cancellationToken).ConfigureAwait(false);

        var label = picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name;
        var artifact = new ImageArtifact(data, imagePart.ContentType, slideNumber, streamInfo.FileName, label);
        artifact.Metadata[MetadataKeys.Slide] = slideNumber.ToString(CultureInfo.InvariantCulture);

        var context = ConversionContextAccessor.Current;
        var provider = context?.Providers.Image ?? imageUnderstandingProvider;
        var request = context?.Request.Intelligence.Image;

        if (provider is not null)
        {
            try
            {
                await using var analysisStream = imageHandle.OpenRead();
                var result = await provider.AnalyzeAsync(analysisStream, streamInfo, request, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(result?.Caption))
                {
                    artifact.Metadata[MetadataKeys.Caption] = result!.Caption!;
                    artifact.Label ??= result.Caption;
                }

                if (!string.IsNullOrWhiteSpace(result?.Text))
                {
                    artifact.Metadata[MetadataKeys.OcrText] = result!.Text!;
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

    private static void AppendTextShape(DocumentFormat.OpenXml.Presentation.Shape shape, StringBuilder markdown, StringBuilder text)
    {
        var textBody = shape.TextBody;
        if (textBody is null)
        {
            return;
        }

        var placeholderShape = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape;
        var placeholderType = placeholderShape?.Type?.Value;
        var isTitle = placeholderType == PlaceholderValues.Title ||
                      placeholderType == PlaceholderValues.CenteredTitle ||
                      placeholderType == PlaceholderValues.SubTitle;

        foreach (var paragraph in textBody.Elements<A.Paragraph>())
        {
            var paragraphText = new StringBuilder();

            foreach (var run in paragraph.Elements<A.Run>())
            {
                var runProperties = run.RunProperties;
                var runText = run.Text?.Text ?? string.Empty;
                if (string.IsNullOrEmpty(runText))
                {
                    continue;
                }

                if (runProperties?.Bold?.Value == true)
                {
                    runText = $"**{runText}**";
                }

                if (runProperties?.Italic?.Value == true)
                {
                    runText = $"*{runText}*";
                }

                paragraphText.Append(runText);
            }

            foreach (var textElement in paragraph.Elements<A.Text>())
            {
                if (!string.IsNullOrWhiteSpace(textElement.Text))
                {
                    paragraphText.Append(textElement.Text);
                }
            }

            var finalText = paragraphText.ToString().Trim();
            if (string.IsNullOrWhiteSpace(finalText))
            {
                continue;
            }

            if (isTitle)
            {
                markdown.AppendLine($"### {finalText}");
            }
            else
            {
                markdown.AppendLine(finalText);
            }

            markdown.AppendLine();
            text.AppendLine(finalText);
        }
    }

    private static string ExtractTitle(string? rawText, string? fileName, string markdown)
    {
        var fromRaw = ExtractTitleCore(rawText);
        if (!string.IsNullOrWhiteSpace(fromRaw))
        {
            return fromRaw!;
        }

        var fromMarkdown = ExtractTitleCore(markdown);
        if (!string.IsNullOrWhiteSpace(fromMarkdown))
        {
            return fromMarkdown!;
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            if (!string.IsNullOrWhiteSpace(nameWithoutExtension))
            {
                return nameWithoutExtension;
            }
        }

        return "PowerPoint Presentation";
    }

    private static string? ExtractTitleCore(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines.Take(10))
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("###", StringComparison.Ordinal))
            {
                return trimmedLine.TrimStart('#').Trim();
            }
        }

        foreach (var line in lines.Take(5))
        {
            var trimmedLine = line.Trim();
            if (!trimmedLine.StartsWith("##", StringComparison.Ordinal) && trimmedLine.Length > 5 && trimmedLine.Length < 100)
            {
                return trimmedLine;
            }
        }

        return null;
    }
}
