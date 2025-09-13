using System.Text;
using System.Text.Json;

namespace MarkItDown.Core.Converters;

/// <summary>
/// Converter for Jupyter Notebook (.ipynb) files that extracts code and markdown cells.
/// </summary>
public sealed class JupyterNotebookConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ipynb"
    };

    private static readonly HashSet<string> AcceptedMimeTypePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/x-ipynb+json"
    };

    public int Priority => 180; // More specific than JSON converter

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

        // For .ipynb files, we can also try to peek at the content if it's seekable
        if (!stream.CanSeek)
            return true;

        try
        {
            var originalPosition = stream.Position;
            stream.Position = 0;

            // Read a small chunk to check for notebook structure
            var buffer = new byte[1024];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            var content = Encoding.UTF8.GetString(buffer, 0, bytesRead).TrimStart();

            stream.Position = originalPosition;

            // Simple check for Jupyter notebook structure
            return content.Contains("\"nbformat\"") && content.Contains("\"cells\"");
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
            var notebookContent = await reader.ReadToEndAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(notebookContent))
                return new DocumentConverterResult(string.Empty);

            var markdown = new StringBuilder();
            var title = ExtractTitleFromFileName(streamInfo.FileName) ?? "Jupyter Notebook";

            markdown.AppendLine($"# {title}");
            markdown.AppendLine();

            // Parse the notebook
            using var document = JsonDocument.Parse(notebookContent);
            var root = document.RootElement;

            // Extract metadata
            if (root.TryGetProperty("metadata", out var metadata))
            {
                var notebookMetadata = ExtractNotebookMetadata(metadata);
                if (!string.IsNullOrEmpty(notebookMetadata))
                {
                    markdown.AppendLine(notebookMetadata);
                    markdown.AppendLine();
                }
            }

            // Process cells
            if (root.TryGetProperty("cells", out var cells) && cells.ValueKind == JsonValueKind.Array)
            {
                var cellNumber = 1;
                foreach (var cell in cells.EnumerateArray())
                {
                    ProcessCell(cell, markdown, cellNumber++);
                }
            }

            return new DocumentConverterResult(
                markdown: markdown.ToString().TrimEnd(),
                title: title
            );
        }
        catch (JsonException ex)
        {
            throw new FileConversionException($"Invalid Jupyter Notebook format: {ex.Message}", ex);
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
        {
            throw new FileConversionException($"Failed to convert Jupyter Notebook: {ex.Message}", ex);
        }
    }

    private static string ExtractNotebookMetadata(JsonElement metadata)
    {
        var metadataMarkdown = new StringBuilder();

        if (metadata.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
        {
            metadataMarkdown.AppendLine($"**Title:** {title.GetString()}");
        }

        if (metadata.TryGetProperty("authors", out var authors))
        {
            var authorList = ExtractAuthors(authors);
            if (!string.IsNullOrEmpty(authorList))
            {
                metadataMarkdown.AppendLine($"**Authors:** {authorList}");
            }
        }

        if (metadata.TryGetProperty("kernelspec", out var kernelspec) && 
            kernelspec.TryGetProperty("display_name", out var kernelName) && 
            kernelName.ValueKind == JsonValueKind.String)
        {
            metadataMarkdown.AppendLine($"**Kernel:** {kernelName.GetString()}");
        }

        if (metadata.TryGetProperty("language_info", out var languageInfo) && 
            languageInfo.TryGetProperty("name", out var language) && 
            language.ValueKind == JsonValueKind.String)
        {
            metadataMarkdown.AppendLine($"**Language:** {language.GetString()}");
        }

        return metadataMarkdown.ToString().TrimEnd();
    }

    private static string ExtractAuthors(JsonElement authors)
    {
        var authorList = new List<string>();

        if (authors.ValueKind == JsonValueKind.Array)
        {
            foreach (var author in authors.EnumerateArray())
            {
                if (author.ValueKind == JsonValueKind.String)
                {
                    authorList.Add(author.GetString() ?? string.Empty);
                }
                else if (author.ValueKind == JsonValueKind.Object && 
                         author.TryGetProperty("name", out var name) && 
                         name.ValueKind == JsonValueKind.String)
                {
                    authorList.Add(name.GetString() ?? string.Empty);
                }
            }
        }
        else if (authors.ValueKind == JsonValueKind.String)
        {
            authorList.Add(authors.GetString() ?? string.Empty);
        }

        return string.Join(", ", authorList.Where(a => !string.IsNullOrEmpty(a)));
    }

    private static void ProcessCell(JsonElement cell, StringBuilder markdown, int cellNumber)
    {
        if (!cell.TryGetProperty("cell_type", out var cellType) || cellType.ValueKind != JsonValueKind.String)
            return;

        var type = cellType.GetString();
        var hasSource = cell.TryGetProperty("source", out var source);

        switch (type)
        {
            case "markdown":
                if (hasSource)
                {
                    var markdownContent = ExtractCellSource(source);
                    if (!string.IsNullOrWhiteSpace(markdownContent))
                    {
                        markdown.AppendLine(markdownContent);
                        markdown.AppendLine();
                    }
                }
                break;

            case "code":
                if (hasSource)
                {
                    var codeContent = ExtractCellSource(source);
                    if (!string.IsNullOrWhiteSpace(codeContent))
                    {
                        markdown.AppendLine($"## Code Cell {cellNumber}");
                        markdown.AppendLine();
                        
                        // Determine language from metadata if available
                        var language = GetCodeLanguage(cell);
                        
                        markdown.AppendLine($"```{language}");
                        markdown.AppendLine(codeContent);
                        markdown.AppendLine("```");
                        markdown.AppendLine();

                        // Add outputs if present
                        if (cell.TryGetProperty("outputs", out var outputs) && outputs.ValueKind == JsonValueKind.Array)
                        {
                            ProcessCellOutputs(outputs, markdown);
                        }
                    }
                }
                break;

            case "raw":
                if (hasSource)
                {
                    var rawContent = ExtractCellSource(source);
                    if (!string.IsNullOrWhiteSpace(rawContent))
                    {
                        markdown.AppendLine($"## Raw Cell {cellNumber}");
                        markdown.AppendLine();
                        markdown.AppendLine("```");
                        markdown.AppendLine(rawContent);
                        markdown.AppendLine("```");
                        markdown.AppendLine();
                    }
                }
                break;
        }
    }

    private static string ExtractCellSource(JsonElement source)
    {
        if (source.ValueKind == JsonValueKind.Array)
        {
            var lines = new List<string>();
            foreach (var line in source.EnumerateArray())
            {
                if (line.ValueKind == JsonValueKind.String)
                {
                    lines.Add(line.GetString() ?? string.Empty);
                }
            }
            return string.Join("", lines).TrimEnd();
        }
        else if (source.ValueKind == JsonValueKind.String)
        {
            return source.GetString()?.TrimEnd() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string GetCodeLanguage(JsonElement cell)
    {
        // Try to get language from cell metadata
        if (cell.TryGetProperty("metadata", out var metadata) &&
            metadata.TryGetProperty("language", out var language) &&
            language.ValueKind == JsonValueKind.String)
        {
            return language.GetString() ?? "python";
        }

        return "python"; // Default for Jupyter notebooks
    }

    private static void ProcessCellOutputs(JsonElement outputs, StringBuilder markdown)
    {
        var hasOutput = false;

        foreach (var output in outputs.EnumerateArray())
        {
            if (!output.TryGetProperty("output_type", out var outputType) || 
                outputType.ValueKind != JsonValueKind.String)
                continue;

            var type = outputType.GetString();

            if (!hasOutput)
            {
                markdown.AppendLine("**Output:**");
                markdown.AppendLine();
                hasOutput = true;
            }

            switch (type)
            {
                case "stream":
                    if (output.TryGetProperty("text", out var text))
                    {
                        var streamOutput = ExtractCellSource(text);
                        if (!string.IsNullOrWhiteSpace(streamOutput))
                        {
                            markdown.AppendLine("```");
                            markdown.AppendLine(streamOutput);
                            markdown.AppendLine("```");
                            markdown.AppendLine();
                        }
                    }
                    break;

                case "execute_result":
                case "display_data":
                    ProcessDisplayData(output, markdown);
                    break;

                case "error":
                    ProcessErrorOutput(output, markdown);
                    break;
            }
        }
    }

    private static void ProcessDisplayData(JsonElement output, StringBuilder markdown)
    {
        if (!output.TryGetProperty("data", out var data))
            return;

        // Try to get text representation first
        if (data.TryGetProperty("text/plain", out var textPlain))
        {
            var textOutput = ExtractCellSource(textPlain);
            if (!string.IsNullOrWhiteSpace(textOutput))
            {
                markdown.AppendLine("```");
                markdown.AppendLine(textOutput);
                markdown.AppendLine("```");
                markdown.AppendLine();
            }
        }
        // Then try HTML representation
        else if (data.TryGetProperty("text/html", out var htmlContent))
        {
            var htmlOutput = ExtractCellSource(htmlContent);
            if (!string.IsNullOrWhiteSpace(htmlOutput))
            {
                markdown.AppendLine("```html");
                markdown.AppendLine(htmlOutput);
                markdown.AppendLine("```");
                markdown.AppendLine();
            }
        }
    }

    private static void ProcessErrorOutput(JsonElement output, StringBuilder markdown)
    {
        var errorInfo = new StringBuilder();

        if (output.TryGetProperty("ename", out var ename) && ename.ValueKind == JsonValueKind.String)
        {
            errorInfo.AppendLine($"**Error:** {ename.GetString()}");
        }

        if (output.TryGetProperty("evalue", out var evalue) && evalue.ValueKind == JsonValueKind.String)
        {
            errorInfo.AppendLine($"**Message:** {evalue.GetString()}");
        }

        if (output.TryGetProperty("traceback", out var traceback))
        {
            var tracebackText = ExtractCellSource(traceback);
            if (!string.IsNullOrWhiteSpace(tracebackText))
            {
                errorInfo.AppendLine("**Traceback:**");
                errorInfo.AppendLine("```");
                errorInfo.AppendLine(tracebackText);
                errorInfo.AppendLine("```");
            }
        }

        if (errorInfo.Length > 0)
        {
            markdown.AppendLine(errorInfo.ToString());
            markdown.AppendLine();
        }
    }

    private static string? ExtractTitleFromFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrEmpty(nameWithoutExtension) ? null : nameWithoutExtension;
    }
}