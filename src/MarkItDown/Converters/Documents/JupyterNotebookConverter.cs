using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;
using MarkItDown;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for Jupyter Notebook (.ipynb) files that extracts code and markdown cells.
/// </summary>
public sealed class JupyterNotebookConverter : DocumentPipelineConverterBase
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ipynb"
    };

    private static readonly IReadOnlyCollection<string> AcceptedMimeTypePrefixes = new[]
    {
        MimeHelper.GetMimeType(".ipynb") ?? "application/x-ipynb+json",
        MimeHelper.JSON,
    };

    private readonly SegmentOptions segmentOptions;
    private readonly IConversionPipeline conversionPipeline;

    public JupyterNotebookConverter(SegmentOptions? segmentOptions = null, IConversionPipeline? pipeline = null)
        : base(priority: 180)
    {
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
        conversionPipeline = pipeline ?? ConversionPipeline.Empty;
    }

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = streamInfo.ResolveMimeType();
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && AcceptedExtensions.Contains(extension))
            return true;

        return StreamInfo.MatchesMime(normalizedMime, AcceptedMimeTypePrefixes)
            || StreamInfo.MatchesMime(streamInfo.MimeType, AcceptedMimeTypePrefixes);
    }

    private SegmentOptions ResolveSegmentOptions()
        => ConversionContextAccessor.Current?.Segments ?? segmentOptions;

    private ArtifactStorageOptions ResolveStorageOptions()
        => ConversionContextAccessor.Current?.Storage ?? ArtifactStorageOptions.Default;

    public override bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (!AcceptsInput(streamInfo))
            return false;

        if (!stream.CanSeek)
            return true;

        try
        {
            var originalPosition = stream.Position;
            stream.Position = 0;

            Span<byte> buffer = stackalloc byte[1024];
            var bytesRead = stream.Read(buffer);
            var content = Encoding.UTF8.GetString(buffer[..bytesRead]).TrimStart();

            stream.Position = originalPosition;

            return content.IndexOf("\"nbformat\"", StringComparison.Ordinal) >= 0 &&
                   content.IndexOf("\"cells\"", StringComparison.Ordinal) >= 0;
        }
        catch
        {
            if (stream.CanSeek)
                stream.Position = 0;
            return true;
        }
    }

    public override async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        ArtifactWorkspace? workspace = null;
        var storageOptions = ResolveStorageOptions();
        var effectiveSegments = ResolveSegmentOptions();
        string? storedSourcePath = null;
        string? storedMarkdownPath = null;

        try
        {
            await using var source = await MaterializeSourceAsync(stream, streamInfo, ".ipynb", cancellationToken).ConfigureAwait(false);

            var defaultMime = AcceptedMimeTypePrefixes.FirstOrDefault() ?? "application/x-ipynb+json";
            workspace = WorkspacePersistence.CreateWorkspace(streamInfo, effectiveSegments, storageOptions, source.FilePath, ".ipynb", defaultMime, cancellationToken, out storedSourcePath);

            await using var fileStream = OpenReadOnlyFile(source.FilePath);
            using var reader = new StreamReader(fileStream, streamInfo.Charset ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
            var notebookContent = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(notebookContent))
            {
                var emptySegments = new List<DocumentSegment>
                {
                    new DocumentSegment(string.Empty, SegmentType.Page, 1, "Page 1", source: streamInfo.FileName)
                };

                var generatedAtEmpty = DateTime.UtcNow;
                var titleEmpty = ExtractTitleFromFileName(streamInfo.FileName) ?? "Jupyter Notebook";
                var metadataEmpty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [MetadataKeys.DocumentTitle] = titleEmpty,
                    [MetadataKeys.DocumentPages] = "0",
                    [MetadataKeys.DocumentImages] = "0",
                    [MetadataKeys.DocumentTables] = "0",
                    [MetadataKeys.WorkspaceDirectory] = workspace.DirectoryPath
                };

                if (!string.IsNullOrWhiteSpace(storedSourcePath))
                {
                    metadataEmpty[MetadataKeys.WorkspaceSourceFile] = storedSourcePath;
                }

                storedMarkdownPath = WorkspacePersistence.PersistMarkdown(workspace, storageOptions, streamInfo, string.Empty, cancellationToken);
                if (!string.IsNullOrWhiteSpace(storedMarkdownPath))
                {
                    metadataEmpty[MetadataKeys.WorkspaceMarkdownFile] = storedMarkdownPath;
                }

                return DocumentConverterResult.FromFactory(
                    () => string.Empty,
                    titleEmpty,
                    emptySegments,
                    new ConversionArtifacts(),
                    metadataEmpty,
                    artifactDirectory: workspace.DirectoryPath,
                    cleanup: null,
                    asyncCleanup: workspace,
                    generatedAtUtc: generatedAtEmpty);
            }

            using var document = JsonDocument.Parse(notebookContent);
            var root = document.RootElement;

            var sections = new List<CellContent>();
            var metadataEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var title = ExtractTitleFromFileName(streamInfo.FileName) ?? "Jupyter Notebook";

            sections.Add(new CellContent("Notebook Title", $"# {title}", SegmentType.Section, "title"));

            if (root.TryGetProperty("metadata", out var metadataElement))
            {
                var metadataMarkdown = ExtractNotebookMetadata(metadataElement, metadataEntries);
                if (!string.IsNullOrWhiteSpace(metadataMarkdown))
                {
                    sections.Add(new CellContent("Notebook Metadata", metadataMarkdown, SegmentType.Metadata, "metadata"));
                }
            }

            if (root.TryGetProperty("cells", out var cellsElement) && cellsElement.ValueKind == JsonValueKind.Array)
            {
                var cellIndex = 1;
                var markdownCount = 0;
                var codeCount = 0;
                var rawCount = 0;

                foreach (var cell in cellsElement.EnumerateArray())
                {
                    var cellResult = ConvertCell(cell, cellIndex++);
                    if (cellResult is null || string.IsNullOrWhiteSpace(cellResult.Markdown))
                    {
                        continue;
                    }

                    sections.Add(cellResult);

                    switch (cellResult.CellType)
                    {
                        case "markdown":
                            markdownCount++;
                            break;
                        case "code":
                            codeCount++;
                            break;
                        case "raw":
                            rawCount++;
                            break;
                    }
                }

                metadataEntries[MetadataKeys.NotebookCellsCount] = (markdownCount + codeCount + rawCount).ToString(CultureInfo.InvariantCulture);
                metadataEntries[MetadataKeys.NotebookCellsMarkdown] = markdownCount.ToString(CultureInfo.InvariantCulture);
                metadataEntries[MetadataKeys.NotebookCellsCode] = codeCount.ToString(CultureInfo.InvariantCulture);
                metadataEntries[MetadataKeys.NotebookCellsRaw] = rawCount.ToString(CultureInfo.InvariantCulture);
            }

            var extraction = BuildExtraction(sections, streamInfo);

            foreach (var pair in metadataEntries)
            {
                extraction.Artifacts.Metadata[pair.Key] = pair.Value;
            }

            await conversionPipeline.ExecuteAsync(streamInfo, extraction.Artifacts, extraction.Segments, cancellationToken).ConfigureAwait(false);

            var generatedAt = DateTime.UtcNow;
            var meta = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, streamInfo, segmentOptions, title, generatedAt);
            var finalTitle = meta.Title ?? title;

            var documentMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataKeys.DocumentTitle] = finalTitle,
                [MetadataKeys.DocumentPages] = extraction.Segments.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentImages] = extraction.Artifacts.Images.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentTables] = extraction.Artifacts.Tables.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.WorkspaceDirectory] = workspace.DirectoryPath
            };

            foreach (var entry in extraction.Artifacts.Metadata)
            {
                documentMetadata[entry.Key] = entry.Value;
            }

            if (!string.IsNullOrWhiteSpace(storedSourcePath))
            {
                documentMetadata[MetadataKeys.WorkspaceSourceFile] = storedSourcePath;
            }

            storedMarkdownPath = WorkspacePersistence.PersistMarkdown(workspace, storageOptions, streamInfo, meta.Markdown, cancellationToken);
            if (!string.IsNullOrWhiteSpace(storedMarkdownPath))
            {
                documentMetadata[MetadataKeys.WorkspaceMarkdownFile] = storedMarkdownPath;
            }

            string MarkdownFactory()
            {
                var recomposed = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, streamInfo, effectiveSegments, title, generatedAt);
                return recomposed.Markdown;
            }

            return DocumentConverterResult.FromFactory(
                MarkdownFactory,
                finalTitle,
                extraction.Segments,
                extraction.Artifacts,
                documentMetadata,
                artifactDirectory: workspace.DirectoryPath,
                cleanup: null,
                asyncCleanup: workspace,
                generatedAtUtc: generatedAt);
        }
        catch (JsonException ex)
        {
            if (workspace is not null)
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
            }

            throw new FileConversionException($"Invalid Jupyter Notebook format: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not MarkItDownException)
        {
            if (workspace is not null)
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
            }

            throw new FileConversionException($"Failed to convert Jupyter Notebook: {ex.Message}", ex);
        }
        catch
        {
            if (workspace is not null)
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private static string ExtractNotebookMetadata(JsonElement metadata, IDictionary<string, string> metadataEntries)
    {
        var markdown = new StringBuilder();

        if (metadata.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
        {
            var value = title.GetString();
            markdown.AppendLine($"**Title:** {value}");
            metadataEntries[MetadataKeys.NotebookMetadataTitle] = value ?? string.Empty;
        }

        if (metadata.TryGetProperty("authors", out var authors))
        {
            var authorList = ExtractAuthors(authors);
            if (!string.IsNullOrWhiteSpace(authorList))
            {
                markdown.AppendLine($"**Authors:** {authorList}");
                metadataEntries[MetadataKeys.NotebookMetadataAuthors] = authorList;
            }
        }

        if (metadata.TryGetProperty("kernelspec", out var kernel) &&
            kernel.TryGetProperty("display_name", out var kernelName) &&
            kernelName.ValueKind == JsonValueKind.String)
        {
            var value = kernelName.GetString();
            markdown.AppendLine($"**Kernel:** {value}");
            metadataEntries[MetadataKeys.NotebookMetadataKernel] = value ?? string.Empty;
        }

        if (metadata.TryGetProperty("language_info", out var languageInfo) &&
            languageInfo.TryGetProperty("name", out var language) &&
            language.ValueKind == JsonValueKind.String)
        {
            var value = language.GetString();
            markdown.AppendLine($"**Language:** {value}");
            metadataEntries[MetadataKeys.NotebookMetadataLanguage] = value ?? string.Empty;
        }

        return markdown.ToString().TrimEnd();
    }

    private static string ExtractAuthors(JsonElement authors)
    {
        var names = new List<string>();

        if (authors.ValueKind == JsonValueKind.Array)
        {
            foreach (var author in authors.EnumerateArray())
            {
                if (author.ValueKind == JsonValueKind.String)
                {
                    names.Add(author.GetString() ?? string.Empty);
                }
                else if (author.ValueKind == JsonValueKind.Object &&
                         author.TryGetProperty("name", out var name) &&
                         name.ValueKind == JsonValueKind.String)
                {
                    names.Add(name.GetString() ?? string.Empty);
                }
            }
        }
        else if (authors.ValueKind == JsonValueKind.String)
        {
            names.Add(authors.GetString() ?? string.Empty);
        }

        return string.Join(", ", names.Where(static author => !string.IsNullOrWhiteSpace(author)));
    }

    private static CellContent? ConvertCell(JsonElement cell, int cellNumber)
    {
        if (!cell.TryGetProperty("cell_type", out var cellType) || cellType.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var type = cellType.GetString() ?? "unknown";
        var label = type switch
        {
            "markdown" => $"Markdown Cell {cellNumber}",
            "code" => $"Code Cell {cellNumber}",
            "raw" => $"Raw Cell {cellNumber}",
            _ => $"Cell {cellNumber}"
        };

        if (!cell.TryGetProperty("source", out var source))
        {
            return null;
        }

        var builder = new StringBuilder();

        switch (type)
        {
            case "markdown":
            {
                var content = ExtractCellSource(source);
                if (string.IsNullOrWhiteSpace(content))
                {
                    return null;
                }

                builder.AppendLine(content.TrimEnd());
                return new CellContent(label, builder.ToString().TrimEnd(), SegmentType.Section, type);
            }

            case "code":
            {
                var codeContent = ExtractCellSource(source);
                if (string.IsNullOrWhiteSpace(codeContent))
                {
                    return null;
                }

                var language = GetCodeLanguage(cell);
                builder.AppendLine($"## {label}");
                builder.AppendLine();
                builder.AppendLine($"```{language}");
                builder.AppendLine(codeContent.TrimEnd());
                builder.AppendLine("```");

                if (cell.TryGetProperty("outputs", out var outputs) && outputs.ValueKind == JsonValueKind.Array)
                {
                    ProcessCellOutputs(outputs, builder);
                }

                return new CellContent(label, builder.ToString().TrimEnd(), SegmentType.Section, type);
            }

            case "raw":
            {
                var rawContent = ExtractCellSource(source);
                if (string.IsNullOrWhiteSpace(rawContent))
                {
                    return null;
                }

                builder.AppendLine($"## {label}");
                builder.AppendLine();
                builder.AppendLine("```");
                builder.AppendLine(rawContent.TrimEnd());
                builder.AppendLine("```");

                return new CellContent(label, builder.ToString().TrimEnd(), SegmentType.Section, type);
            }

            default:
                return null;
        }
    }

    private static string ExtractCellSource(JsonElement source)
    {
        return source.ValueKind switch
        {
            JsonValueKind.Array => string.Join(string.Empty, EnumerateStrings(source)).TrimEnd(),
            JsonValueKind.String => source.GetString()?.TrimEnd() ?? string.Empty,
            _ => string.Empty
        };
    }

    private static IEnumerable<string> EnumerateStrings(JsonElement array)
    {
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                yield return element.GetString() ?? string.Empty;
            }
        }
    }

    private static string GetCodeLanguage(JsonElement cell)
    {
        if (cell.TryGetProperty("metadata", out var metadata) &&
            metadata.TryGetProperty("language", out var language) &&
            language.ValueKind == JsonValueKind.String)
        {
            return language.GetString() ?? "python";
        }

        return "python";
    }

    private static void ProcessCellOutputs(JsonElement outputs, StringBuilder builder)
    {
        var hasOutput = false;

        foreach (var output in outputs.EnumerateArray())
        {
            if (!output.TryGetProperty("output_type", out var outputType) || outputType.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var type = outputType.GetString();
            if (string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            if (!hasOutput)
            {
                builder.AppendLine();
                builder.AppendLine("**Output:**");
                builder.AppendLine();
                hasOutput = true;
            }

            switch (type)
            {
                case "stream":
                    if (output.TryGetProperty("text", out var text))
                    {
                        var content = ExtractCellSource(text);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            builder.AppendLine("```");
                            builder.AppendLine(content.TrimEnd());
                            builder.AppendLine("```");
                            builder.AppendLine();
                        }
                    }
                    break;

                case "display_data":
                case "execute_result":
                    if (output.TryGetProperty("data", out var data))
                    {
                        AppendRichOutput(data, builder);
                    }
                    break;

                case "error":
                    AppendErrorOutput(output, builder);
                    break;
            }
        }
    }

    private static void AppendRichOutput(JsonElement data, StringBuilder builder)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (data.TryGetProperty("text/markdown", out var markdown))
        {
            var content = ExtractCellSource(markdown);
            if (!string.IsNullOrWhiteSpace(content))
            {
                builder.AppendLine(content.TrimEnd());
                builder.AppendLine();
            }
        }
        else if (data.TryGetProperty("text/plain", out var text))
        {
            var content = ExtractCellSource(text);
            if (!string.IsNullOrWhiteSpace(content))
            {
                builder.AppendLine("```");
                builder.AppendLine(content.TrimEnd());
                builder.AppendLine("```");
                builder.AppendLine();
            }
        }
    }

    private static void AppendErrorOutput(JsonElement output, StringBuilder builder)
    {
        var errorBuilder = new StringBuilder();

        if (output.TryGetProperty("ename", out var ename) && ename.ValueKind == JsonValueKind.String)
        {
            errorBuilder.Append(ename.GetString());
        }

        if (output.TryGetProperty("evalue", out var evalue) && evalue.ValueKind == JsonValueKind.String)
        {
            if (errorBuilder.Length > 0)
            {
                errorBuilder.Append(": ");
            }

            errorBuilder.Append(evalue.GetString());
        }

        if (errorBuilder.Length > 0)
        {
            builder.AppendLine($"`{errorBuilder}`");
        }

        if (output.TryGetProperty("traceback", out var traceback))
        {
            var traceContent = ExtractCellSource(traceback);
            if (!string.IsNullOrWhiteSpace(traceContent))
            {
                builder.AppendLine();
                builder.AppendLine("```");
                builder.AppendLine(traceContent.TrimEnd());
                builder.AppendLine("```");
            }
        }

        builder.AppendLine();
    }

    private static NotebookExtractionResult BuildExtraction(IReadOnlyList<CellContent> cells, StreamInfo streamInfo)
    {
        var segments = new List<DocumentSegment>();
        var artifacts = new ConversionArtifacts();

        var index = 0;
        foreach (var cell in cells)
        {
            if (string.IsNullOrWhiteSpace(cell.Markdown))
            {
                continue;
            }

            var pageNumber = ++index;
            var metadata = new Dictionary<string, string>
            {
            [MetadataKeys.NotebookCellIndex] = pageNumber.ToString(CultureInfo.InvariantCulture),
            [MetadataKeys.NotebookCellLabel] = cell.Label,
            [MetadataKeys.NotebookCellType] = cell.CellType
            };

            var segment = new DocumentSegment(
                cell.Markdown,
                cell.Type,
                pageNumber,
                cell.Label,
                source: streamInfo.FileName,
                additionalMetadata: metadata);

            segments.Add(segment);
            artifacts.TextBlocks.Add(new TextArtifact(cell.Markdown, pageNumber, streamInfo.FileName, cell.Label));
        }

        if (segments.Count == 0)
        {
            var emptySegment = new DocumentSegment(string.Empty, SegmentType.Page, 1, "Page 1", source: streamInfo.FileName);
            segments.Add(emptySegment);
        }

        return new NotebookExtractionResult(segments, artifacts);
    }

    private static string? ExtractTitleFromFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var name = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private sealed record CellContent(string Label, string Markdown, SegmentType Type, string CellType);

    private sealed record NotebookExtractionResult(List<DocumentSegment> Segments, ConversionArtifacts Artifacts);
}
