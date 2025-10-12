using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ManagedCode.MimeTypes;
using MarkItDown;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for OPML outline documents.
/// </summary>
public sealed class OpmlConverter : StructuredXmlConverterBase
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".opml",
    };

    private static readonly string OpmlMime = MimeHelper.GetMimeType(".opml")
        ?? "application/opml+xml";

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        OpmlMime,
        MimeHelper.XML,
    };

    protected override IReadOnlyCollection<string> AcceptedExtensions => Extensions;

    protected override IReadOnlyCollection<string> AcceptedMimeTypes => MimeTypes;

    public OpmlConverter(SegmentOptions? segmentOptions = null, IConversionPipeline? pipeline = null)
        : base(segmentOptions, pipeline)
    {
    }

    protected override string RenderMarkdown(XDocument document)
    {
        if (document.Root is null)
        {
            return string.Empty;
        }

        var outlines = document.Root
            .Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals("body", StringComparison.OrdinalIgnoreCase))?
            .Elements();

        if (outlines is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var outline in outlines)
        {
            RenderOutline(outline, builder, 0);
        }

        return builder.ToString().Trim();
    }

    private static void RenderOutline(XElement outline, StringBuilder builder, int depth)
    {
        var text = outline.Attribute("text")?.Value ?? outline.Attribute("title")?.Value;
        if (text is null)
        {
            return;
        }

        builder.Append(new string(' ', depth * 2));
        builder.Append("- ");
        builder.AppendLine(text.Trim());

        foreach (var child in outline.Elements(outline.Name.Namespace + "outline"))
        {
            RenderOutline(child, builder, depth + 1);
        }
    }
}
