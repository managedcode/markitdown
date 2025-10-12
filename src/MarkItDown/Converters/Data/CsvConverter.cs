using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;
using nietras.SeparatedValues;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for CSV files that creates Markdown tables.
/// </summary>
public sealed class CsvConverter : DocumentConverterBase
{
    public CsvConverter()
        : base(priority: 200) // More specific than plain text
    {
    }

    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv",
        ".tsv",
        ".tab",
    };

    private static readonly IReadOnlyCollection<string> AcceptedMimeTypePrefixes = new List<string>
    {
        MimeHelper.CSV,
        MimeHelper.GetMimeType(".tsv") ?? "text/tab-separated-values",
        "application/csv",
    };

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = streamInfo.ResolveMimeType();
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && AcceptedExtensions.Contains(extension))
            return true;

        return StreamInfo.MatchesMime(normalizedMime, AcceptedMimeTypePrefixes)
            || StreamInfo.MatchesMime(streamInfo.MimeType, AcceptedMimeTypePrefixes);
    }

    public override async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            // Reset stream position
            if (stream.CanSeek)
                stream.Position = 0;

            using var reader = new StreamReader(stream, streamInfo.Charset ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var normalizedMime = streamInfo.ResolveMimeType();
            var isTabSeparated = string.Equals(streamInfo.Extension, ".tsv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(streamInfo.Extension, ".tab", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedMime, MimeHelper.GetMimeType(".tsv") ?? "text/tab-separated-values", StringComparison.OrdinalIgnoreCase);

            using var sepReader = await Sep.Reader(options => options with
            {
                HasHeader = true,
                Unescape = true,
                Trim = SepTrim.All,
                Sep = new Sep(isTabSeparated ? '\t' : ','),
            }).FromAsync(reader, cancellationToken).ConfigureAwait(false);

            var rows = new List<string[]>();
            var maxColumns = 0;

            if (sepReader.HasHeader && !sepReader.Header.IsEmpty)
            {
                var headerNames = sepReader.Header.ColNames;
                if (headerNames.Count > 0)
                {
                    var headerRow = new string[headerNames.Count];
                    for (var i = 0; i < headerNames.Count; i++)
                    {
                        headerRow[i] = EscapeMarkdownTableCell(headerNames[i] ?? string.Empty);
                    }

                    maxColumns = Math.Max(maxColumns, headerRow.Length);
                    rows.Add(headerRow);
                }
            }

            foreach (var row in sepReader)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var values = new string[row.ColCount];
                for (var i = 0; i < row.ColCount; i++)
                {
                    values[i] = EscapeMarkdownTableCell(row[i].ToString() ?? string.Empty);
                }

                maxColumns = Math.Max(maxColumns, values.Length);
                rows.Add(values);
            }

            if (rows.Count == 0)
            {
                return new DocumentConverterResult(string.Empty);
            }

            var markdownTable = CreateMarkdownTable(rows, maxColumns);

            return new DocumentConverterResult(
                markdown: markdownTable,
                title: ExtractTitleFromFileName(streamInfo.FileName)
            );
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
        {
            throw new FileConversionException($"Failed to convert CSV file: {ex.Message}", ex);
        }
    }

    private static string CreateMarkdownTable(List<string[]> rows, int maxColumns)
    {
        if (rows.Count == 0)
            return string.Empty;

        var result = new StringBuilder();

        // Header row is the first line
        var header = PadRow(rows[0], maxColumns);
        result.AppendLine("| " + string.Join(" | ", header) + " |");

        // Separator row
        result.AppendLine("| " + string.Join(" | ", Enumerable.Repeat("---", maxColumns)) + " |");

        for (var i = 1; i < rows.Count; i++)
        {
            var row = PadRow(rows[i], maxColumns);
            result.AppendLine("| " + string.Join(" | ", row) + " |");
        }

        return result.ToString().TrimEnd();
    }

    private static IEnumerable<string> PadRow(string[] row, int maxColumns)
    {
        for (var i = 0; i < maxColumns; i++)
        {
            yield return i < row.Length ? row[i] : string.Empty;
        }
    }

    private static string EscapeMarkdownTableCell(string cell)
    {
        if (string.IsNullOrEmpty(cell))
            return string.Empty;

        // Escape markdown characters in table cells
        return cell
            .Replace("|", "\\|")
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Trim();
    }

    private static string? ExtractTitleFromFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrEmpty(nameWithoutExtension) ? null : nameWithoutExtension;
    }
}
