using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using MarkItDown;

namespace MarkItDown.Converters;

public sealed partial class DocxConverter
{
    private ElementProcessingResult ProcessParagraph(
        ParagraphDescriptor descriptor,
        IReadOnlyDictionary<string, DocxImagePart> imageCatalog,
        StreamInfo streamInfo,
        ImageArtifactPersistor? imagePersistor,
        CancellationToken cancellationToken)
    {
        var paragraphResult = ConvertParagraph(
            descriptor.Paragraph,
            imageCatalog,
            descriptor.PageNumber,
            streamInfo,
            imagePersistor,
            cancellationToken);

        return new ElementProcessingResult(
            descriptor.Index,
            descriptor.PageNumber,
            PageSpan: 1,
            DocxElementKind.Paragraph,
            paragraphResult.Markdown,
            paragraphResult.RawText,
            TableRows: null,
            paragraphResult.Images);
    }

    private ParagraphProcessingResult ConvertParagraph(
        Paragraph paragraph,
        IReadOnlyDictionary<string, DocxImagePart> imageCatalog,
        int pageNumber,
        StreamInfo streamInfo,
        ImageArtifactPersistor? imagePersistor,
        CancellationToken cancellationToken)
    {
        var tokens = new List<IParagraphToken>();
        var textBuffer = new StringBuilder();
        var isHeading = false;
        var headingLevel = 0;
        var hasActiveStyle = false;
        var bufferBold = false;
        var bufferItalic = false;

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

        bool GetBold(RunProperties? properties)
        {
            if (properties?.Bold is not { } bold)
            {
                return false;
            }

            var value = bold.Val;
            return value is null || value.Value;
        }

        bool GetItalic(RunProperties? properties)
        {
            if (properties?.Italic is not { } italic)
            {
                return false;
            }

            var value = italic.Val;
            return value is null || value.Value;
        }

        void FlushText()
        {
            if (textBuffer.Length == 0)
            {
                return;
            }

            tokens.Add(new ParagraphTextToken(textBuffer.ToString(), bufferBold, bufferItalic));
            textBuffer.Clear();
            hasActiveStyle = false;
        }

        void AppendText(string content, bool bold, bool italic)
        {
            if (string.IsNullOrEmpty(content))
            {
                return;
            }

            if (!hasActiveStyle || bufferBold != bold || bufferItalic != italic)
            {
                FlushText();
                bufferBold = bold;
                bufferItalic = italic;
                hasActiveStyle = true;
            }

            textBuffer.Append(content);
        }

        void QueueImage(ImageArtifact artifact)
        {
            FlushText();
            tokens.Add(new ParagraphImageToken(artifact));
        }

        foreach (var run in paragraph.Elements<Run>())
        {
            var runProperties = run.RunProperties;
            var currentBold = GetBold(runProperties);
            var currentItalic = GetItalic(runProperties);

            foreach (var textElement in run.Elements())
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (textElement)
                {
                    case Text text:
                    {
                        var textContent = text.Text;
                        if (!string.IsNullOrEmpty(textContent))
                        {
                            AppendText(textContent, currentBold, currentItalic);
                        }

                        break;
                    }

                    case TabChar:
                        AppendText("\t", currentBold, currentItalic);
                        break;

                    case Break br when br.Type?.Value == BreakValues.TextWrapping:
                        AppendText("\n", currentBold, currentItalic);
                        break;

                    case DocumentFormat.OpenXml.Wordprocessing.Drawing drawing:
                    {
                        var artifact = TryCreateImageArtifact(drawing, imageCatalog, pageNumber, streamInfo, imagePersistor, cancellationToken);
                        if (artifact is not null)
                        {
                            QueueImage(artifact);
                        }

                        break;
                    }

                    case Picture picture:
                    {
                        var artifact = TryCreateImageArtifact(picture, imageCatalog, pageNumber, streamInfo, imagePersistor, cancellationToken);
                        if (artifact is not null)
                        {
                            QueueImage(artifact);
                        }

                        break;
                    }
                }
            }
        }

        FlushText();

        var paragraphBuilder = new StringBuilder();
        var collectedImages = new List<ImageArtifact>();

        foreach (var token in tokens)
        {
            switch (token)
            {
                case ParagraphTextToken textToken:
                    AppendFormattedText(textToken);
                    break;

                case ParagraphImageToken imageToken:
                {
                    var artifact = imageToken.Artifact;
                    AppendImageContent(paragraphBuilder, artifact);
                    collectedImages.Add(artifact);
                    break;
                }
            }
        }

        void AppendFormattedText(ParagraphTextToken token)
        {
            var text = token.Text;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (isHeading)
            {
                paragraphBuilder.Append(text);
                return;
            }

            var span = text.AsSpan();
            var leading = CountLeadingWhitespace(span);
            var trailing = CountTrailingWhitespace(span);
            var contentLength = span.Length - leading - trailing;

            if (contentLength <= 0)
            {
                paragraphBuilder.Append(span);
                return;
            }

            if (leading > 0)
            {
                paragraphBuilder.Append(span[..leading]);
            }

            var core = span.Slice(leading, contentLength).ToString();
            paragraphBuilder.Append(FormatCore(core, token.Bold, token.Italic));

            if (trailing > 0)
            {
                paragraphBuilder.Append(span[^trailing..]);
            }
        }

        static int CountLeadingWhitespace(ReadOnlySpan<char> value)
        {
            var count = 0;
            while (count < value.Length && char.IsWhiteSpace(value[count]))
            {
                count++;
            }

            return count;
        }

        static int CountTrailingWhitespace(ReadOnlySpan<char> value)
        {
            var count = value.Length - 1;
            var trailing = 0;

            while (count >= 0 && char.IsWhiteSpace(value[count]))
            {
                trailing++;
                count--;
            }

            return trailing;
        }

        static string FormatCore(string value, bool bold, bool italic)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            if (bold && italic)
            {
                return $"***{value}***";
            }

            if (bold)
            {
                return $"**{value}**";
            }

            if (italic)
            {
                return $"*{value}*";
            }

            return value;
        }

        var finalText = paragraphBuilder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(finalText) && collectedImages.Count == 0)
        {
            return new ParagraphProcessingResult(string.Empty, string.Empty, Array.Empty<ImageArtifact>());
        }

        if (isHeading && headingLevel > 0 && !string.IsNullOrWhiteSpace(finalText))
        {
            finalText = $"{new string('#', headingLevel)} {finalText}";
        }

        return collectedImages.Count == 0
            ? new ParagraphProcessingResult(finalText, finalText, Array.Empty<ImageArtifact>())
            : new ParagraphProcessingResult(finalText, finalText, collectedImages);
    }

}
