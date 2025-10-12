using System.Collections.Generic;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using MarkItDown;

namespace MarkItDown.Converters;

public sealed partial class DocxConverter
{
    private sealed class DocxExtractionResult
    {
        public DocxExtractionResult(List<DocumentSegment> segments, ConversionArtifacts artifacts, string rawText)
        {
            Segments = segments;
            Artifacts = artifacts;
            RawText = rawText;
        }

        public List<DocumentSegment> Segments { get; }

        public ConversionArtifacts Artifacts { get; }

        public string RawText { get; }
    }

    private sealed record DocxImagePart(byte[] Data, string ContentType);

    private enum DocxElementKind
    {
        Paragraph,
        Table,
        Other
    }

    private abstract record DocxElementDescriptor(int Index, int PageNumber, DocxElementKind Kind);

    private sealed record ParagraphDescriptor(int Index, int PageNumber, Paragraph Paragraph)
        : DocxElementDescriptor(Index, PageNumber, DocxElementKind.Paragraph);

    private sealed record TableDescriptor(int Index, int PageNumber, Table Table)
        : DocxElementDescriptor(Index, PageNumber, DocxElementKind.Table);

    private sealed record OtherDescriptor(int Index, int PageNumber, OpenXmlElement Element)
        : DocxElementDescriptor(Index, PageNumber, DocxElementKind.Other);

    private sealed record ParagraphProcessingResult(string Markdown, string RawText, IReadOnlyList<ImageArtifact> Images);

    private sealed record ElementProcessingResult(
        int Index,
        int PageNumber,
        int PageSpan,
        DocxElementKind Kind,
        string Markdown,
        string RawText,
        IList<IList<string>>? TableRows,
        IReadOnlyList<ImageArtifact> Images);

    private interface IParagraphToken
    {
    }

    private sealed record ParagraphTextToken(string Text, bool Bold, bool Italic) : IParagraphToken;

    private sealed record ParagraphImageToken(ImageArtifact Artifact) : IParagraphToken;

    private sealed class PageAccumulator
    {
        public StringBuilder Markdown { get; } = new();

        public List<ImageArtifact> Images { get; } = new();
    }


}
