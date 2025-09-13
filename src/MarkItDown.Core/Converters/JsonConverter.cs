using System.Text;
using System.Text.Json;

namespace MarkItDown.Core.Converters;

/// <summary>
/// Converter for JSON files that creates structured Markdown with proper formatting.
/// </summary>
public sealed class JsonConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".jsonl", ".ndjson"
    };

    private static readonly HashSet<string> AcceptedMimeTypePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/json", "application/ld+json", "text/json"
    };

    public int Priority => 150; // Higher priority than plain text for JSON files

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
        if (!AcceptsInput(streamInfo))
            return false;

        // For JSON, we can also try to peek at the content if it's seekable
        if (!stream.CanSeek)
            return true;

        try
        {
            var originalPosition = stream.Position;
            stream.Position = 0;

            // Read a small chunk to check for JSON-like content
            var buffer = new byte[1024];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            var content = Encoding.UTF8.GetString(buffer, 0, bytesRead).TrimStart();

            stream.Position = originalPosition;

            // Simple check for JSON-like content
            return content.StartsWith("{") || content.StartsWith("[");
        }
        catch
        {
            // If we can't read, fall back to mime type check
            if (stream.CanSeek)
                stream.Position = 0;
            return true;
        }
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
            var jsonContent = await reader.ReadToEndAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(jsonContent))
                return new DocumentConverterResult(string.Empty);

            var markdown = new StringBuilder();
            var title = ExtractTitleFromFileName(streamInfo.FileName) ?? "JSON Document";

            markdown.AppendLine($"# {title}");
            markdown.AppendLine();

            // Handle different JSON formats
            if (streamInfo.Extension?.ToLowerInvariant() == ".jsonl" || streamInfo.Extension?.ToLowerInvariant() == ".ndjson")
            {
                // Handle JSON Lines format
                await ProcessJsonLines(jsonContent, markdown, cancellationToken);
            }
            else
            {
                // Handle regular JSON
                await ProcessJson(jsonContent, markdown, cancellationToken);
            }

            return new DocumentConverterResult(
                markdown: markdown.ToString().TrimEnd(),
                title: title
            );
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

    private static Task ProcessJson(string jsonContent, StringBuilder markdown, CancellationToken cancellationToken)
    {
        try
        {
            // Parse and reformat JSON for better readability
            using var document = JsonDocument.Parse(jsonContent);
            ConvertJsonElementToMarkdown(document.RootElement, markdown, 0);
        }
        catch (JsonException)
        {
            // If parsing fails, just include as code block
            markdown.AppendLine("```json");
            markdown.AppendLine(jsonContent);
            markdown.AppendLine("```");
        }

        return Task.CompletedTask;
    }

    private static Task ProcessJsonLines(string jsonContent, StringBuilder markdown, CancellationToken cancellationToken)
    {
        var lines = jsonContent.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var lineNumber = 1;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                markdown.AppendLine($"## Line {lineNumber}");
                markdown.AppendLine();

                using var document = JsonDocument.Parse(line.Trim());
                ConvertJsonElementToMarkdown(document.RootElement, markdown, 0);
                markdown.AppendLine();
            }
            catch (JsonException)
            {
                // If parsing fails for this line, include as code
                markdown.AppendLine($"## Line {lineNumber} (Invalid JSON)");
                markdown.AppendLine();
                markdown.AppendLine("```");
                markdown.AppendLine(line);
                markdown.AppendLine("```");
                markdown.AppendLine();
            }

            lineNumber++;
        }

        return Task.CompletedTask;
    }

    private static void ConvertJsonElementToMarkdown(JsonElement element, StringBuilder markdown, int depth)
    {
        var indent = new string(' ', depth * 2);

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var propertyName = FormatPropertyName(property.Name);

                    if (property.Value.ValueKind == JsonValueKind.Object || property.Value.ValueKind == JsonValueKind.Array)
                    {
                        if (depth == 0)
                            markdown.AppendLine($"## {propertyName}");
                        else
                            markdown.AppendLine($"{indent}**{propertyName}:**");
                        markdown.AppendLine();

                        ConvertJsonElementToMarkdown(property.Value, markdown, depth + 1);
                    }
                    else
                    {
                        var value = GetJsonValueAsString(property.Value);
                        markdown.AppendLine($"{indent}**{propertyName}:** {value}");
                    }
                }
                break;

            case JsonValueKind.Array:
                var index = 1;
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
                    {
                        markdown.AppendLine($"{indent}### Item {index}");
                        markdown.AppendLine();
                        ConvertJsonElementToMarkdown(item, markdown, depth + 1);
                    }
                    else
                    {
                        var value = GetJsonValueAsString(item);
                        markdown.AppendLine($"{indent}- {value}");
                    }
                    index++;
                }
                break;

            default:
                var scalarValue = GetJsonValueAsString(element);
                markdown.AppendLine($"{indent}{scalarValue}");
                break;
        }

        if (depth == 0)
            markdown.AppendLine();
    }

    private static string GetJsonValueAsString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            JsonValueKind.Undefined => "undefined",
            _ => element.GetRawText()
        };
    }

    private static string FormatPropertyName(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return propertyName;

        // Convert camelCase or snake_case to readable format
        var result = new StringBuilder();
        var previousChar = '\0';

        for (int i = 0; i < propertyName.Length; i++)
        {
            var currentChar = propertyName[i];

            if (i == 0)
            {
                result.Append(char.ToUpper(currentChar));
            }
            else if (currentChar == '_' && i < propertyName.Length - 1)
            {
                result.Append(' ');
                result.Append(char.ToUpper(propertyName[i + 1]));
                i++; // Skip the next character as we've already processed it
            }
            else if (char.IsUpper(currentChar) && char.IsLower(previousChar))
            {
                result.Append(' ');
                result.Append(currentChar);
            }
            else
            {
                result.Append(currentChar);
            }

            previousChar = currentChar;
        }

        return result.ToString();
    }

    private static string? ExtractTitleFromFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrEmpty(nameWithoutExtension) ? null : nameWithoutExtension;
    }
}