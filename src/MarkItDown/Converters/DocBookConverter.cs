using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for DocBook XML documents.
/// </summary>
public sealed class DocBookConverter : StructuredXmlConverterBase
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".docbook",
        ".dbk",
    };

    private static readonly string DocBookMime = MimeHelper.GetMimeType(".docbook")
        ?? MimeTypeUtilities.WithSubtype(MimeHelper.XML, "docbook+xml");

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        DocBookMime,
        MimeHelper.XML,
    };

    protected override IReadOnlyCollection<string> AcceptedExtensions => Extensions;

    protected override IReadOnlyCollection<string> AcceptedMimeTypes => MimeTypes;

    protected override string RenderMarkdown(XDocument document)
    {
        var builder = new StringBuilder();
        if (document.Root is null)
        {
            return string.Empty;
        }

        RenderElement(document.Root, builder, 1);
        return builder.ToString().Trim();
    }

    private static void RenderElement(XElement element, StringBuilder builder, int level)
    {
        var name = element.Name.LocalName.ToLowerInvariant();
        switch (name)
        {
            case "book":
            case "article":
            case "chapter":
            case "section":
            case "sect1":
            case "sect2":
            case "sect3":
            case "sect4":
            case "sect5":
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
                        RenderElement(child, builder, level + 1);
                    }
                }

                break;
            case "title":
            case "subtitle":
                builder.AppendLine(new string('#', Math.Min(level, 6)) + " " + RenderTextNodes(element.Nodes()));
                builder.AppendLine();
                break;
            case "para":
            case "simpara":
            case "p":
                builder.AppendLine(RenderTextNodes(element.Nodes()));
                builder.AppendLine();
                break;
            case "itemizedlist":
            case "orderedlist":
                RenderList(element, builder, level, name == "itemizedlist" ? "-" : "1.");
                break;
            case "listitem":
                builder.Append(RenderTextNodes(element.Nodes()));
                break;
            case "emphasis":
            case "bold":
            case "link":
                builder.Append(RenderTextNodes(new[] { element }));
                break;
            default:
                foreach (var child in element.Elements())
                {
                    RenderElement(child, builder, level);
                }

                if (!element.HasElements)
                {
                    var text = element.Value?.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        builder.AppendLine(text);
                        builder.AppendLine();
                    }
                }

                break;
        }
    }

    private static void RenderList(XElement element, StringBuilder builder, int level, string bullet)
    {
        var index = 0;
        foreach (var item in element.Elements("listitem"))
        {
            var marker = bullet == "1." ? $"{++index}." : "-";
            builder.Append(new string(' ', (level - 1) * 2));
            builder.Append(marker);
            builder.Append(' ');
            builder.AppendLine(RenderTextNodes(item.Nodes()));
        }

        builder.AppendLine();
    }
}
