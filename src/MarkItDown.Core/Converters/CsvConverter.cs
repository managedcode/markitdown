using System.Text;

namespace MarkItDown.Core.Converters;

/// <summary>
/// Converter for CSV files that creates Markdown tables.
/// </summary>
public sealed class CsvConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv"
    };

    private static readonly HashSet<string> AcceptedMimeTypePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/csv", "application/csv"
    };

    public int Priority => 200; // More specific than plain text

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var mimeType = streamInfo.MimeType?.ToLowerInvariant() ?? string.Empty;
        var extension = streamInfo.Extension?.ToLowerInvariant();

        // Check the extension first
        if (extension is not null && AcceptedExtensions.Contains(extension))
            return true;

        // Check the mime type
        foreach (var prefix in AcceptedMimeTypePrefixes)
        {
            if (mimeType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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

            // Read the content
            using var reader = new StreamReader(stream, streamInfo.Charset ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var content = await reader.ReadToEndAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(content))
                return new DocumentConverterResult(string.Empty);

            // Parse CSV content
            var rows = ParseCsvContent(content);

            if (rows.Count == 0)
                return new DocumentConverterResult(string.Empty);

            // Create markdown table
            var markdownTable = CreateMarkdownTable(rows);

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

    private static List<List<string>> ParseCsvContent(string content)
    {
        var rows = new List<List<string>>();
        var lines = content.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var row = ParseCsvLine(line.Trim());
            if (row.Count > 0)
                rows.Add(row);
        }

        return rows;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var currentField = new StringBuilder();
        var inQuotes = false;
        var i = 0;

        while (i < line.Length)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Escaped quote
                    currentField.Append('"');
                    i += 2;
                }
                else
                {
                    // Toggle quote state
                    inQuotes = !inQuotes;
                    i++;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                // Field separator
                fields.Add(currentField.ToString());
                currentField.Clear();
                i++;
            }
            else
            {
                currentField.Append(c);
                i++;
            }
        }

        // Add the last field
        fields.Add(currentField.ToString());

        return fields;
    }

    private static string CreateMarkdownTable(List<List<string>> rows)
    {
        if (rows.Count == 0)
            return string.Empty;

        var result = new StringBuilder();
        var maxColumns = rows.Max(r => r.Count);

        // Ensure all rows have the same number of columns
        foreach (var row in rows)
        {
            while (row.Count < maxColumns)
                row.Add(string.Empty);
        }

        // Add header row
        result.AppendLine("| " + string.Join(" | ", rows[0].Select(EscapeMarkdownTableCell)) + " |");

        // Add separator row
        result.AppendLine("| " + string.Join(" | ", Enumerable.Repeat("---", maxColumns)) + " |");

        // Add data rows
        for (var i = 1; i < rows.Count; i++)
        {
            result.AppendLine("| " + string.Join(" | ", rows[i].Select(EscapeMarkdownTableCell)) + " |");
        }

        return result.ToString().TrimEnd();
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