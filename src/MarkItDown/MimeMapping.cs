namespace MarkItDown;

using System.Linq;
using ManagedCode.MimeTypes;

internal static class MimeMapping
{ 
    private const string OctetStream = "application/octet-stream";

    private static readonly Dictionary<string, string> ExtensionToMime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".adoc"] = GetMimeOrDefault(".adoc", MimeTypeUtilities.Compose(MimeHelper.TEXT, "asciidoc")),
        [".asciidoc"] = GetMimeOrDefault(".asciidoc", MimeTypeUtilities.Compose(MimeHelper.TEXT, "asciidoc")),
        [".atom"] = MimeHelper.ATOM,
        [".bib"] = GetMimeOrDefault(".bib", MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-bibtex")),
        [".bibtex"] = GetMimeOrDefault(".bibtex", MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-bibtex")),
        [".bits"] = MimeTypeUtilities.WithSubtype(MimeHelper.XML, "bits+xml"),
        [".bmp"] = MimeHelper.BMP,
        [".metamd"] = GetMimeOrDefault(".metamd", MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-metamd")),
        [".creole"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-creole"),
        [".csv"] = MimeHelper.CSV,
        [".csljson"] = GetMimeOrDefault(".csljson", MimeTypeUtilities.Compose(MimeHelper.APPLICATION, "vnd.citationstyles.csl+json")),
        [".dbk"] = MimeTypeUtilities.WithSubtype(MimeHelper.XML, "docbook+xml"),
        [".dj"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-djot"),
        [".djot"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-djot"),
        [".docbook"] = MimeTypeUtilities.WithSubtype(MimeHelper.XML, "docbook+xml"),
        [".docx"] = MimeHelper.DOCX,
        [".dot"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "vnd.graphviz"),
        [".dokuwiki"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-dokuwiki"),
        [".eml"] = MimeHelper.EML,
        [".endnote"] = GetMimeOrDefault(".endnote", MimeTypeUtilities.Compose(MimeHelper.APPLICATION, "x-endnote-refer")),
        [".enl"] = GetMimeOrDefault(".enl", MimeTypeUtilities.Compose(MimeHelper.APPLICATION, "x-endnote-refer")),
        [".epub"] = MimeHelper.EPUB,
        [".fb2"] = MimeTypeUtilities.Compose(MimeHelper.APPLICATION, "fb2+zip"),
        [".gif"] = MimeHelper.GIF,
        [".gv"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "vnd.graphviz"),
        [".htm"] = MimeHelper.HTML,
        [".html"] = MimeHelper.HTML,
        [".ipynb"] = GetMimeOrDefault(".ipynb", MimeTypeUtilities.Compose(MimeHelper.APPLICATION, "x-ipynb+json")),
        [".jats"] = MimeTypeUtilities.WithSubtype(MimeHelper.XML, "jats+xml"),
        [".jpg"] = MimeHelper.JPEG,
        [".jpeg"] = MimeHelper.JPEG,
        [".json"] = MimeHelper.JSON,
        [".jsonl"] = MimeHelper.JSON,
        [".latex"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-latex"),
        [".markdown"] = MimeHelper.MARKDOWN,
        [".md"] = MimeHelper.MARKDOWN,
        [".mediawiki"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-mediawiki"),
        [".mermaid"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-mermaid"),
        [".mmd"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-mermaid"),
        [".m4a"] = MimeHelper.MP4A,
        [".msg"] = MimeHelper.MSG,
        [".mp3"] = MimeHelper.MP3,
        [".mp4"] = MimeHelper.MP4,
        [".ndjson"] = MimeHelper.JSON,
        [".odt"] = GetMimeOrDefault(".odt", MimeTypeUtilities.Compose(MimeHelper.APPLICATION, "vnd.oasis.opendocument.text")),
        [".opml"] = GetMimeOrDefault(".opml", MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-opml")),
        [".org"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-org"),
        [".pdf"] = MimeHelper.PDF,
        [".plantuml"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-plantuml"),
        [".png"] = MimeHelper.PNG,
        [".pptx"] = MimeHelper.PPTX,
        [".puml"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-plantuml"),
        [".rest"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "prs.fallenstein.rst"),
        [".ris"] = GetMimeOrDefault(".ris", MimeTypeUtilities.Compose(MimeHelper.APPLICATION, "x-research-info-systems")),
        [".rst"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "prs.fallenstein.rst"),
        [".rss"] = MimeHelper.RSS,
        [".rtf"] = MimeHelper.RTF,
        [".tab"] = MimeTypeUtilities.WithSubtype(MimeHelper.CSV, "tab-separated-values"),
        [".tex"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-tex"),
        [".textile"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-textile"),
        [".tif"] = MimeHelper.TIFF,
        [".tiff"] = MimeHelper.TIFF,
        [".tikz"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-tikz"),
        [".tsv"] = MimeTypeUtilities.WithSubtype(MimeHelper.CSV, "tab-separated-values"),
        [".typ"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-typst"),
        [".typst"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-typst"),
        [".txt"] = MimeHelper.TEXT,
        [".wav"] = MimeHelper.WAV,
        [".webp"] = MimeHelper.WEBP,
        [".wiki"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-wiki"),
        [".wsd"] = MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-plantuml"),
        [".xhtml"] = MimeHelper.XHTML,
        [".xlsx"] = MimeHelper.XLSX,
        [".xml"] = MimeHelper.XML,
        [".xsl"] = MimeHelper.XML,
        [".xsd"] = MimeHelper.XML,
        [".xslt"] = MimeHelper.XML,
        [".zip"] = MimeHelper.ZIP,
    };

    private static readonly Dictionary<string, string> MimeToExtension = ExtensionToMime
        .GroupBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

    public static string? GetMimeType(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        return ExtensionToMime.TryGetValue(normalized, out var mime) ? mime : null;
    }

    public static string? GetExtension(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return null;
        }

        return MimeToExtension.TryGetValue(mimeType, out var extension) ? extension : null;
    }

    private static string GetMimeOrDefault(string extension, string fallbackMime)
    {
        var mime = MimeHelper.GetMimeType(extension);
        return string.IsNullOrWhiteSpace(mime) || string.Equals(mime, OctetStream, StringComparison.OrdinalIgnoreCase)
            ? fallbackMime
            : mime;
    }
}
