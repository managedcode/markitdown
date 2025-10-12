using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ManagedCode.MimeTypes;
using MarkItDown;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for JATS XML documents.
/// </summary>
public sealed class JatsConverter : StructuredXmlConverterBase
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".jats",
        ".bits",
    };

    private static readonly string JatsMime = MimeHelper.GetMimeType(".jats")
        ?? "application/jats+xml";

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        JatsMime,
        MimeHelper.XML,
    };

    protected override IReadOnlyCollection<string> AcceptedExtensions => Extensions;

    protected override IReadOnlyCollection<string> AcceptedMimeTypes => MimeTypes;

    public JatsConverter(SegmentOptions? segmentOptions = null, IConversionPipeline? pipeline = null)
        : base(segmentOptions, pipeline)
    {
    }

    protected override string RenderMarkdown(XDocument document)
    {
        if (document.Root is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        RenderElement(document.Root, builder, 1);
        return builder.ToString().Trim();
    }

    protected override string? ExtractTitle(XDocument document)
    {
        var articleTitle = document
            .Root?
            .Descendants(document.Root.GetDefaultNamespace() + "article-title")
            .FirstOrDefault();

        return articleTitle?.Value?.Trim() ?? base.ExtractTitle(document);
    }

    private static void RenderElement(XElement element, StringBuilder builder, int level)
    {
        var localName = element.Name.LocalName.ToLowerInvariant();
        switch (localName)
        {
            case "article":
            case "front":
            case "body":
            case "sec":
                foreach (var child in element.Elements())
                {
                    if (child.Name.LocalName.Equals("title", StringComparison.OrdinalIgnoreCase))
                    {
                        var headingLevel = Math.Min(level, 6);
                        builder.AppendLine(new string('#', headingLevel) + " " + RenderTextNodes(child.Nodes()));
                        builder.AppendLine();
                    }
                    else
                    {
                        var nextLevel = child.Name.LocalName.Equals("sec", StringComparison.OrdinalIgnoreCase)
                            ? level + 1
                            : level;
                        RenderElement(child, builder, nextLevel);
                    }
                }

                break;
            case "title":
                builder.AppendLine(new string('#', Math.Min(level, 6)) + " " + RenderTextNodes(element.Nodes()));
                builder.AppendLine();
                break;
            case "p":
            case "para":
                builder.AppendLine(RenderTextNodes(element.Nodes()));
                builder.AppendLine();
                break;
            case "list" when element.Attribute("list-type")?.Value == "bullet":
            case "list" when element.Attribute("list-type")?.Value == "order":
            case "list":
                RenderList(element, builder, level);
                break;
            default:
                foreach (var child in element.Elements())
                {
                    RenderElement(child, builder, level);
                }

                if (!element.HasElements)
                {
                    var value = element.Value?.Trim();
                    if (!string.IsNullOrEmpty(value))
                    {
                        builder.AppendLine(value);
                        builder.AppendLine();
                    }
                }

                break;
        }
    }

    private static void RenderList(XElement element, StringBuilder builder, int level)
    {
        var ordered = string.Equals(element.Attribute("list-type")?.Value, "order", StringComparison.OrdinalIgnoreCase);
        var marker = ordered ? 1 : 0;
        foreach (var item in element.Elements(element.Name.Namespace + "list-item"))
        {
            var prefix = ordered ? $"{marker++}." : "-";
            builder.Append(new string(' ', (level - 1) * 2));
            builder.Append(prefix);
            builder.Append(' ');
            builder.AppendLine(RenderTextNodes(item.Nodes()));
        }

        builder.AppendLine();
    }
}
