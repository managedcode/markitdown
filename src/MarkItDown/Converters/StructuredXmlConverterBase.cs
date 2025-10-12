using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

public abstract class StructuredXmlConverterBase : IDocumentConverter
{
    protected abstract IReadOnlyCollection<string> AcceptedExtensions { get; }

    protected abstract IReadOnlyCollection<string> AcceptedMimeTypes { get; }

    public virtual int Priority => 150;

    public virtual bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = MimeTypeUtilities.NormalizeMime(streamInfo);
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && AcceptedExtensions.Contains(extension))
        {
            return true;
        }

        if (MimeTypeUtilities.MatchesAny(normalizedMime, AcceptedMimeTypes))
        {
            return true;
        }

        return MimeTypeUtilities.MatchesAny(streamInfo.MimeType, AcceptedMimeTypes);
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        return AcceptsInput(streamInfo);
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        var document = await LoadDocumentAsync(stream, cancellationToken).ConfigureAwait(false);
        var markdown = RenderMarkdown(document);
        return new DocumentConverterResult(markdown, ExtractTitle(document));
    }

    protected virtual async Task<XDocument> LoadDocumentAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return XDocument.Parse(content, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
    }

    protected abstract string RenderMarkdown(XDocument document);

    protected virtual string? ExtractTitle(XDocument document)
    {
        return document.Root?.Element(document.Root.GetDefaultNamespace() + "title")?.Value
            ?? document.Root?.Element("title")?.Value;
    }

    internal static string RenderTextNodes(IEnumerable<XNode> nodes)
    {
        var builder = new StringBuilder();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case XText text:
                    builder.Append(text.Value);
                    break;
                case XElement element:
                    builder.Append(RenderElementInline(element));
                    break;
            }
        }

        return builder.ToString();
    }

    private static string RenderElementInline(XElement element)
    {
        var content = RenderTextNodes(element.Nodes());
        var name = element.Name.LocalName.ToLowerInvariant();
        return name switch
        {
            "emphasis" or "em" or "i" => $"*{content}*",
            "bold" or "strong" or "b" => $"**{content}**",
            "link" or "a" =>
                element.Attribute("href") is { } href ? $"[{content}]({href.Value})" : content,
            "sub" => $"~{content}~",
            "sup" => $"^{content}^",
            _ => content,
        };
    }
}
