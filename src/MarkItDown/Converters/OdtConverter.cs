using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for OpenDocument Text (ODT) packages.
/// </summary>
public sealed class OdtConverter : IDocumentConverter
{
    private const string ContentEntryName = "content.xml";

    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".odt",
    };

    private static readonly string OdtMime = MimeHelper.GetMimeType(".odt")
        ?? MimeTypeUtilities.Compose("application", "vnd.oasis.opendocument.text");

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        OdtMime,
    };

    public int Priority => 180;

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = MimeTypeUtilities.NormalizeMime(streamInfo);
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && Extensions.Contains(extension))
        {
            return true;
        }

        return MimeTypeUtilities.MatchesAny(normalizedMime, MimeTypes)
            || MimeTypeUtilities.MatchesAny(streamInfo.MimeType, MimeTypes);
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        return AcceptsInput(streamInfo);
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (!stream.CanSeek)
        {
            throw new FileConversionException("ODT conversion requires a seekable stream.");
        }

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(ContentEntryName);
        if (entry is null)
        {
            return new DocumentConverterResult(string.Empty);
        }

        await using var entryStream = entry.Open();
        var document = await XDocument.LoadAsync(entryStream, LoadOptions.PreserveWhitespace, cancellationToken).ConfigureAwait(false);
        var markdown = RenderMarkdown(document);
        return new DocumentConverterResult(markdown, ExtractTitle(document));
    }

    private static string RenderMarkdown(XDocument document)
    {
        var builder = new StringBuilder();
        var officeNs = document.Root?.Name.Namespace ?? XNamespace.None;
        var textNs = (XNamespace)"urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        var body = document.Root?
            .Element(officeNs + "body")?
            .Element(officeNs + "text");

        if (body is null)
        {
            return string.Empty;
        }

        foreach (var node in body.Elements())
        {
            var localName = node.Name.LocalName;
            if (node.Name == textNs + "h")
            {
                var levelAttribute = node.Attribute(textNs + "outline-level");
                var level = 1;
                if (levelAttribute is not null && int.TryParse(levelAttribute.Value, out var parsed))
                {
                    level = Math.Clamp(parsed, 1, 6);
                }

                builder.AppendLine(new string('#', level) + " " + StructuredXmlConverterBase.RenderTextNodes(node.Nodes()));
                builder.AppendLine();
            }
            else if (node.Name == textNs + "p")
            {
                var text = StructuredXmlConverterBase.RenderTextNodes(node.Nodes());
                if (!string.IsNullOrWhiteSpace(text))
                {
                    builder.AppendLine(text.Trim());
                    builder.AppendLine();
                }
            }
        }

        return builder.ToString().Trim();
    }

    private static string? ExtractTitle(XDocument document)
    {
        var officeNs = document.Root?.Name.Namespace ?? XNamespace.None;
        var metaNs = (XNamespace)"urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
        return document.Root?
            .Element(officeNs + "meta")?
            .Element(metaNs + "title")?
            .Value;
    }
}
