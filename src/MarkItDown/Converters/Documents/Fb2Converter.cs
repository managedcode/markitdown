using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ManagedCode.MimeTypes;
using MarkItDown;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for FictionBook (FB2) XML documents.
/// </summary>
public sealed class Fb2Converter : StructuredXmlConverterBase
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".fb2",
    };

    private static readonly string Fb2Mime = MimeHelper.GetMimeType(".fb2")
        ?? "application/fb2+xml";

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        Fb2Mime,
        MimeHelper.XML,
    };

    protected override IReadOnlyCollection<string> AcceptedExtensions => Extensions;

    protected override IReadOnlyCollection<string> AcceptedMimeTypes => MimeTypes;

    public Fb2Converter(SegmentOptions? segmentOptions = null, IConversionPipeline? pipeline = null)
        : base(segmentOptions, pipeline)
    {
    }

    protected override string? ExtractTitle(XDocument document)
    {
        var ns = document.Root?.GetDefaultNamespace() ?? XNamespace.None;
        return document.Root?
            .Element(ns + "description")?
            .Element(ns + "title-info")?
            .Element(ns + "book-title")?
            .Value?.Trim();
    }

    protected override string RenderMarkdown(XDocument document)
    {
        if (document.Root is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var ns = document.Root.GetDefaultNamespace();
        foreach (var body in document.Root.Elements(ns + "body"))
        {
            var title = body.Element(ns + "title")?.Elements(ns + "p").FirstOrDefault()?.Value;
            if (!string.IsNullOrWhiteSpace(title))
            {
                builder.AppendLine($"# {title.Trim()}");
                builder.AppendLine();
            }

            foreach (var section in body.Elements(ns + "section"))
            {
                RenderSection(section, builder, 2, ns);
            }
        }

        return builder.ToString().Trim();
    }

    private static void RenderSection(XElement section, StringBuilder builder, int level, XNamespace ns)
    {
        var title = section.Element(ns + "title")?.Elements(ns + "p").FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.AppendLine(new string('#', Math.Min(level, 6)) + " " + title.Trim());
            builder.AppendLine();
        }

        foreach (var paragraph in section.Elements(ns + "p"))
        {
            builder.AppendLine(RenderTextNodes(paragraph.Nodes()));
            builder.AppendLine();
        }

        foreach (var subsection in section.Elements(ns + "section"))
        {
            RenderSection(subsection, builder, level + 1, ns);
        }
    }
}
