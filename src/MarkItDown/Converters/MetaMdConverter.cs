using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for MetaMD documents that carry structured metadata and references.
/// </summary>
public sealed class MetaMdConverter : IDocumentConverter
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".metamd",
        ".metamd.md",
    };

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        MimeHelper.GetMimeType(".metamd") ?? MimeTypeUtilities.Compose(MimeHelper.TEXT, "x-metamd"),
    };

    private static readonly Regex FrontMatterPattern = new("^\\+\\+\\+\\s*\\n(?<meta>.*?)(?:\\n\\+\\+\\+\\s*\\n)(?<body>[\\s\\S]*)$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ReferencePattern = new("\\[@(?<id>[^\\]]+)\\]", RegexOptions.Compiled);
    private static readonly Regex DiagramPattern = new(":::diagram\\s+type=\"(?<type>[^\"]+)\"\\s*\\n(?<content>[\\s\\S]*?)\\n:::", RegexOptions.Compiled);

    public int Priority => 145;

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = MimeTypeUtilities.NormalizeMime(streamInfo);
        if (!string.IsNullOrEmpty(streamInfo.FileName) && streamInfo.FileName.EndsWith(".metamd.md", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var extension = streamInfo.Extension?.ToLowerInvariant();
        if (extension is not null && Extensions.Contains(extension))
        {
            return true;
        }

        return MimeTypeUtilities.MatchesAny(normalizedMime, MimeTypes)
            || MimeTypeUtilities.MatchesAny(streamInfo.MimeType, MimeTypes);
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default) => AcceptsInput(streamInfo);

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var (metadata, body) = ParseDocument(content);
        var (processedBody, usedReferences) = ProcessBody(body, metadata.References);
        var markdown = ComposeMarkdown(metadata, processedBody, usedReferences);
        return new DocumentConverterResult(markdown, metadata.Title ?? streamInfo.FileName);
    }

    private static (MetaMdMetadata Metadata, string Body) ParseDocument(string content)
    {
        var match = FrontMatterPattern.Match(content);
        if (!match.Success)
        {
            return (MetaMdMetadata.Empty, content);
        }

        var meta = match.Groups["meta"].Value;
        var body = match.Groups["body"].Value;

        try
        {
            using var document = JsonDocument.Parse(meta, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            return (MetaMdMetadata.FromJson(document.RootElement), body);
        }
        catch (JsonException)
        {
            return (MetaMdMetadata.Empty, body);
        }
    }

    private static (string Body, List<MetaMdReference> UsedReferences) ProcessBody(string body, IReadOnlyDictionary<string, MetaMdReference> references)
    {
        var used = new List<MetaMdReference>();
        var replacedDiagrams = DiagramPattern.Replace(body, match =>
        {
            var type = match.Groups["type"].Value;
            var content = match.Groups["content"].Value.Trim();
            return $"```{type}\n{content}\n```";
        });

        var replacedReferences = ReferencePattern.Replace(replacedDiagrams, match =>
        {
            var id = match.Groups["id"].Value.Trim();
            if (references.TryGetValue(id, out var reference))
            {
                if (used.All(r => !string.Equals(r.Id, reference.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    used.Add(reference);
                }

                return reference.Url is not null
                    ? $"[{reference.Title}]({reference.Url})"
                    : $"**{reference.Title}**";
            }

            return match.Value;
        });

        return (replacedReferences, used);
    }

    private static string ComposeMarkdown(MetaMdMetadata metadata, string body, IReadOnlyCollection<MetaMdReference> references)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(metadata.Title))
        {
            builder.AppendLine("# " + metadata.Title!.Trim());
            builder.AppendLine();
        }

        if (metadata.Contributors.Count > 0)
        {
            builder.AppendLine("**Contributors:** " + string.Join(", ", metadata.Contributors));
        }

        if (metadata.Affiliations.Count > 0)
        {
            builder.AppendLine("**Affiliations:** " + string.Join(", ", metadata.Affiliations));
        }

        if (metadata.Keywords.Count > 0)
        {
            builder.AppendLine("**Keywords:** " + string.Join(", ", metadata.Keywords));
        }

        if (!string.IsNullOrWhiteSpace(metadata.Abstract))
        {
            builder.AppendLine();
            builder.AppendLine(metadata.Abstract.Trim());
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.AppendLine(body.Trim());

        if (references.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## References");
            foreach (var reference in references)
            {
                builder.Append("- **");
                builder.Append(reference.Title);
                builder.Append("**");
                if (reference.Authors.Count > 0)
                {
                    builder.Append(" — ");
                    builder.Append(string.Join(", ", reference.Authors));
                }

                if (!string.IsNullOrWhiteSpace(reference.Url))
                {
                    builder.Append($" [link]({reference.Url})");
                }

                builder.AppendLine();
            }
        }

        return builder.ToString().Trim();
    }

    private readonly record struct MetaMdMetadata(
        string? Title,
        string? Abstract,
        IReadOnlyList<string> Contributors,
        IReadOnlyList<string> Affiliations,
        IReadOnlyList<string> Keywords,
        IReadOnlyDictionary<string, MetaMdReference> References)
    {
        public static MetaMdMetadata Empty { get; } = new(
            null,
            null,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, MetaMdReference>(StringComparer.OrdinalIgnoreCase));

        public static MetaMdMetadata FromJson(JsonElement element)
        {
            var title = element.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
            var abstractText = element.TryGetProperty("abstract", out var abstractElement) ? abstractElement.GetString() : null;
            var contributors = element.TryGetProperty("contributors", out var contributorsElement)
                ? contributorsElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                : new List<string>();
            var affiliations = element.TryGetProperty("affiliations", out var affiliationsElement)
                ? affiliationsElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                : new List<string>();
            var keywords = element.TryGetProperty("keywords", out var keywordsElement)
                ? keywordsElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                : new List<string>();

            var references = new Dictionary<string, MetaMdReference>(StringComparer.OrdinalIgnoreCase);
            if (element.TryGetProperty("references", out var referencesElement) && referencesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var referenceElement in referencesElement.EnumerateArray())
                {
                    var reference = MetaMdReference.FromJson(referenceElement);
                    if (!string.IsNullOrWhiteSpace(reference.Id))
                    {
                        references[reference.Id] = reference;
                    }
                }
            }

            return new MetaMdMetadata(title, abstractText, contributors, affiliations, keywords, references);
        }
    }

    private readonly record struct MetaMdReference(string Id, string Title, IReadOnlyList<string> Authors, string? Url)
    {
        public static MetaMdReference FromJson(JsonElement element)
        {
            var id = element.TryGetProperty("id", out var idProperty) ? idProperty.GetString() ?? string.Empty : string.Empty;
            var title = element.TryGetProperty("title", out var titleProperty) ? titleProperty.GetString() ?? id : id;
            var authors = element.TryGetProperty("authors", out var authorsElement) && authorsElement.ValueKind == JsonValueKind.Array
                ? authorsElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                : new List<string>();
            var url = element.TryGetProperty("url", out var urlProperty) ? urlProperty.GetString() : null;
            return new MetaMdReference(id, title, authors, url);
        }
    }
}
