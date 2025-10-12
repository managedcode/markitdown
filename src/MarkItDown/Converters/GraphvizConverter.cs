using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for Graphviz DOT diagrams.
/// </summary>
public sealed class GraphvizConverter : IDocumentConverter
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".dot",
        ".gv",
    };

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        MimeHelper.GetMimeType(".dot") ?? MimeTypeUtilities.Compose("text", "vnd.graphviz"),
    };

    public int Priority => 129;

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

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default) => AcceptsInput(streamInfo);

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var fenced = new StringBuilder();
        fenced.AppendLine("```dot");
        fenced.AppendLine(content.Trim());
        fenced.AppendLine("```");
        return new DocumentConverterResult(fenced.ToString().Trim(), streamInfo.FileName);
    }
}
