using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for PlantUML diagrams.
/// </summary>
public sealed class PlantUmlConverter : DocumentConverterBase
{
    public PlantUmlConverter()
        : base(priority: 128)
    {
    }

    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".puml",
        ".plantuml",
        ".wsd",
    };

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        MimeHelper.GetMimeType(".puml") ?? "text/x-plantuml",
    };

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = streamInfo.ResolveMimeType();
        var extension = streamInfo.Extension?.ToLowerInvariant();
        if (extension is not null && Extensions.Contains(extension))
        {
            return true;
        }

        return StreamInfo.MatchesMime(normalizedMime, MimeTypes)
            || StreamInfo.MatchesMime(streamInfo.MimeType, MimeTypes);
    }

    public override async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var fenced = new StringBuilder();
        fenced.AppendLine("```plantuml");
        fenced.AppendLine(content.Trim());
        fenced.AppendLine("```");
        return new DocumentConverterResult(fenced.ToString().Trim(), streamInfo.FileName);
    }
}
