using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sylvan.Data.Csv;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for CSV files that creates Markdown tables.
/// </summary>
public sealed class CsvConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv"
    };

    private static readonly IReadOnlyCollection<string> AcceptedMimeTypePrefixes = new List<string>
    {
        MimeHelper.CSV,
        MimeTypeUtilities.WithType(MimeHelper.CSV, "application"),
    };

    public int Priority => 200; // More specific than plain text

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = MimeTypeUtilities.NormalizeMime(streamInfo);
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && AcceptedExtensions.Contains(extension))
            return true;

        return MimeTypeUtilities.MatchesAny(normalizedMime, AcceptedMimeTypePrefixes)
            || MimeTypeUtilities.MatchesAny(streamInfo.MimeType, AcceptedMimeTypePrefixes);
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        return AcceptsInput(streamInfo);
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            // Reset stream position
            if (stream.CanSeek)
                stream.Position = 0;

            using var reader = new StreamReader(stream, streamInfo.Charset ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            using var csv = CsvDataReader.Create(reader, new CsvDataReaderOptions
            {
                HasHeaders = false,
                BufferSize = 64 * 1024,
            });

            if (!await csv.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new DocumentConverterResult(string.Empty);
            }

            var rows = new List<string[]>();
            var maxColumns = 0;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                var values = new string[csv.FieldCount];
                for (var i = 0; i < csv.FieldCount; i++)
                {
                    values[i] = EscapeMarkdownTableCell(csv.IsDBNull(i) ? string.Empty : csv.GetString(i) ?? string.Empty);
                }

                maxColumns = Math.Max(maxColumns, values.Length);
                rows.Add(values);
            }
            while (await csv.ReadAsync(cancellationToken).ConfigureAwait(false));

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
