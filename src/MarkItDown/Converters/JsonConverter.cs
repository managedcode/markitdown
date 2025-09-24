using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for JSON files that renders the payload inside a Markdown code block.
/// </summary>
public sealed class JsonConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".jsonl", ".ndjson"
    };

    private static readonly string[] AcceptedMimeTypePrefixes =
    {
        MimeHelper.JSON,
        MimeHelper.JSONLD,
        MimeTypeUtilities.WithType(MimeHelper.JSON, "text"),
    };

    private static readonly JsonSerializerOptions PrettyPrintOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public int Priority => 150; // Higher priority than plain text for JSON files

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
        if (!AcceptsInput(streamInfo))
            return false;

        // For JSON, we can also try to peek at the content if it's seekable
        if (!stream.CanSeek)
            return true;

        var buffer = ArrayPool<byte>.Shared.Rent(1024);
        try
        {
            var originalPosition = stream.Position;
            stream.Position = 0;

            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            stream.Position = originalPosition;

            if (bytesRead == 0)
            {
                return false;
            }

            var span = StripUtf8Bom(buffer.AsSpan(0, bytesRead));

            foreach (var b in span)
            {
                switch (b)
                {
                    case (byte)'{' or (byte)'[':
                        return true;
                    case (byte)' ':
                    case (byte)'\t':
                    case (byte)'\r':
                    case (byte)'\n':
                        continue;
                    default:
                        return false;
                }
            }

            return false;
        }
        catch
        {
            if (stream.CanSeek)
                stream.Position = 0;
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            // Reset stream position
            if (stream.CanSeek)
                stream.Position = 0;

            using var reader = new StreamReader(stream, streamInfo.Charset ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var jsonContent = await reader.ReadToEndAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                return new DocumentConverterResult(string.Empty);
            }

            var normalizedJson = NormalizeJson(jsonContent, streamInfo.Extension);
            var title = ExtractTitleFromFileName(streamInfo.FileName) ?? "JSON Document";

            var markdown = new StringBuilder(normalizedJson.Length + title.Length + 16);
            markdown.Append("# ");
            markdown.AppendLine(title);
            markdown.AppendLine();
            markdown.AppendLine("```json");
            markdown.Append(normalizedJson);
            markdown.AppendLine();
            markdown.Append("```");

            return new DocumentConverterResult(markdown.ToString(), title);
        }
        catch (JsonException ex)
        {
            throw new FileConversionException($"Invalid JSON format: {ex.Message}", ex);
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
        {
            throw new FileConversionException($"Failed to convert JSON file: {ex.Message}", ex);
        }
    }

    private static string NormalizeJson(string jsonContent, string? extension)
    {
        if (IsJsonLinesExtension(extension))
        {
            return NormalizeJsonLines(jsonContent);
        }

        using var document = JsonDocument.Parse(jsonContent);
        return JsonSerializer.Serialize(document.RootElement, PrettyPrintOptions);
    }

    private static bool IsJsonLinesExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        var normalized = extension.StartsWith('.') ? extension : $".{extension}";
        return string.Equals(normalized, ".jsonl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, ".ndjson", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeJsonLines(string jsonContent)
    {
        var span = jsonContent.AsSpan();
        var builder = new StringBuilder(span.Length);
        var position = 0;
        var wroteLine = false;

        while (position < span.Length)
        {
            var slice = span[position..];
            var delimiterIndex = slice.IndexOfAny('\r', '\n');

            ReadOnlySpan<char> line;
            if (delimiterIndex >= 0)
            {
                line = slice[..delimiterIndex];
                position += delimiterIndex + 1;

                // handle CRLF
                if (slice[delimiterIndex] == '\r' && position < span.Length && span[position] == '\n')
                {
                    position++;
                }
            }
            else
            {
                line = slice;
                position = span.Length;
            }

            line = line.Trim();
            if (line.IsEmpty)
            {
                continue;
            }

            if (wroteLine)
            {
                builder.AppendLine();
            }

            builder.Append(line);
            wroteLine = true;
        }

        return builder.ToString();
    }

    private static string? ExtractTitleFromFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrEmpty(nameWithoutExtension) ? null : nameWithoutExtension;
    }

    private static ReadOnlySpan<byte> StripUtf8Bom(ReadOnlySpan<byte> span)
    {
        return span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF
            ? span[3..]
            : span;
    }
}
