using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ManagedCode.MimeTypes;
using MarkItDown;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for EndNote XML exports.
/// </summary>
public sealed class EndNoteXmlConverter : StructuredXmlConverterBase
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".enl",
        ".endnote",
        ".endnote.xml",
    };

    private static readonly string EndNoteMime = MimeHelper.GetMimeType(".enl")
        ?? "application/endnote+xml";

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        EndNoteMime,
        MimeHelper.XML,
    };

    protected override IReadOnlyCollection<string> AcceptedExtensions => Extensions;

    protected override IReadOnlyCollection<string> AcceptedMimeTypes => MimeTypes;

    public EndNoteXmlConverter(SegmentOptions? segmentOptions = null, IConversionPipeline? pipeline = null)
        : base(segmentOptions, pipeline)
    {
    }

    protected override string RenderMarkdown(XDocument document)
    {
        if (document.Root is null)
        {
            return string.Empty;
        }

        var ns = document.Root.GetDefaultNamespace();
        var records = document.Root.Elements(ns + "record");
        var builder = new StringBuilder();
        foreach (var record in records)
        {
            var title = record.Element(ns + "titles")?.Element(ns + "title")?.Value
                ?? record.Element(ns + "titles")?.Element(ns + "secondary-title")?.Value
                ?? "Untitled";
            var authors = record
                .Element(ns + "contributors")?
                .Elements()
                .SelectMany(e => e.Elements(ns + "name"))
                .Select(e => e.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList() ?? new List<string>();
            var year = record.Element(ns + "dates")?.Element(ns + "year")?.Value;
            var journal = record.Element(ns + "periodical")?.Element(ns + "full-title")?.Value;
            var url = record.Element(ns + "urls")?.Element(ns + "related-urls")?.Elements(ns + "url")?.FirstOrDefault()?.Value;

            builder.Append("- **");
            builder.Append(title.Trim());
            builder.Append("**");

            if (authors.Count > 0)
            {
                builder.Append(" — ");
                builder.Append(string.Join(", ", authors));
            }

            if (!string.IsNullOrWhiteSpace(journal))
            {
                builder.Append(", ");
                builder.Append(journal);
            }

            if (!string.IsNullOrWhiteSpace(year))
            {
                builder.Append(" (" + year + ")");
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                builder.Append($" [link]({url})");
            }

            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }
}
