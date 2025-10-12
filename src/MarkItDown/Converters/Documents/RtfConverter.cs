using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;
using MarkItDown;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for RTF documents with a minimal text extractor.
/// </summary>
public sealed class RtfConverter : DocumentPipelineConverterBase
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".rtf",
    };

    private static readonly string RtfMime = MimeHelper.GetMimeType(".rtf")
        ?? "application/rtf";

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        RtfMime,
    };

    private readonly SegmentOptions segmentOptions;
    private readonly IConversionPipeline conversionPipeline;

    public RtfConverter(SegmentOptions? segmentOptions = null, IConversionPipeline? pipeline = null)
        : base(priority: 190)
    {
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
        conversionPipeline = pipeline ?? ConversionPipeline.Empty;
    }

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

    private SegmentOptions ResolveSegmentOptions()
        => ConversionContextAccessor.Current?.Segments ?? segmentOptions;

    private ArtifactStorageOptions ResolveStorageOptions()
        => ConversionContextAccessor.Current?.Storage ?? ArtifactStorageOptions.Default;

    public override async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        ArtifactWorkspace? workspace = null;
        var storageOptions = ResolveStorageOptions();
        var effectiveSegments = ResolveSegmentOptions();
        string? storedSourcePath = null;
        string? storedMarkdownPath = null;

        try
        {
            await using var source = await MaterializeSourceAsync(stream, streamInfo, ".rtf", cancellationToken).ConfigureAwait(false);

            workspace = WorkspacePersistence.CreateWorkspace(streamInfo, effectiveSegments, storageOptions, source.FilePath, ".rtf", RtfMime, cancellationToken, out storedSourcePath);

            using var fileStream = OpenReadOnlyFile(source.FilePath);
            using var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var rtfContent = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var plainText = ExtractText(rtfContent);
            var extraction = BuildExtraction(plainText, streamInfo);

            await conversionPipeline.ExecuteAsync(streamInfo, extraction.Artifacts, extraction.Segments, cancellationToken).ConfigureAwait(false);

            var generatedAt = DateTime.UtcNow;
            var titleHint = ExtractTitle(extraction.RawText);
            var meta = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, streamInfo, effectiveSegments, titleHint, generatedAt);
            var fallbackTitle = Path.GetFileNameWithoutExtension(streamInfo.FileName ?? streamInfo.LocalPath ?? streamInfo.Url) ?? "RTF Document";
            var title = meta.Title ?? titleHint ?? fallbackTitle;

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataKeys.DocumentTitle] = title,
                [MetadataKeys.DocumentPages] = extraction.Segments.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentImages] = extraction.Artifacts.Images.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentTables] = extraction.Artifacts.Tables.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.WorkspaceDirectory] = workspace.DirectoryPath
            };

            if (!string.IsNullOrWhiteSpace(titleHint))
            {
                metadata[MetadataKeys.DocumentTitleHint] = titleHint!;
            }

            if (!string.IsNullOrWhiteSpace(storedSourcePath))
            {
                metadata[MetadataKeys.WorkspaceSourceFile] = storedSourcePath;
            }

            storedMarkdownPath = WorkspacePersistence.PersistMarkdown(workspace, storageOptions, streamInfo, meta.Markdown, cancellationToken);
            if (!string.IsNullOrWhiteSpace(storedMarkdownPath))
            {
                metadata[MetadataKeys.WorkspaceMarkdownFile] = storedMarkdownPath;
            }

            string MarkdownFactory()
            {
                var recomposed = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, streamInfo, effectiveSegments, titleHint, generatedAt);
                return recomposed.Markdown;
            }

            return DocumentConverterResult.FromFactory(
                MarkdownFactory,
                title,
                extraction.Segments,
                extraction.Artifacts,
                metadata,
                artifactDirectory: workspace.DirectoryPath,
                cleanup: null,
                asyncCleanup: workspace,
                generatedAtUtc: generatedAt);
        }
        catch (Exception ex) when (ex is not MarkItDownException)
        {
            if (workspace is not null)
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
            }

            throw new FileConversionException($"Failed to convert RTF file: {ex.Message}", ex);
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

    private static string ExtractText(string rtf)
    {
        var builder = new StringBuilder();
        var stack = new Stack<int>();
        var i = 0;
        while (i < rtf.Length)
        {
            var ch = rtf[i];
            switch (ch)
            {
                case '{':
                    stack.Push(0);
                    i++;
                    break;
                case '}':
                    if (stack.Count > 0)
                    {
                        stack.Pop();
                    }

                    i++;
                    break;
                case '\\':
                    i++;
                    if (i >= rtf.Length)
                    {
                        break;
                    }

                    var next = rtf[i];
                    if (next == '\\' || next == '{' || next == '}')
                    {
                        builder.Append(next);
                        i++;
                    }
                    else if (next == '\'')
                    {
                        i++;
                        if (i + 1 < rtf.Length)
                        {
                            var hex = rtf.Substring(i, 2);
                            if (byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                            {
                                builder.Append(Encoding.Default.GetString(new[] { value }));
                            }

                            i += 2;
                        }
                    }
                    else
                    {
                        var control = ReadControlWord(rtf, ref i);
                        if (string.Equals(control.Word, "par", StringComparison.OrdinalIgnoreCase) || string.Equals(control.Word, "line", StringComparison.OrdinalIgnoreCase))
                        {
                            builder.AppendLine();
                        }
                        else if (string.Equals(control.Word, "tab", StringComparison.OrdinalIgnoreCase))
                        {
                            builder.Append('\t');
                        }
                        else if (control.Word is not null && control.Word.StartsWith("u", StringComparison.OrdinalIgnoreCase) && control.Argument is not null)
                        {
                            builder.Append(char.ConvertFromUtf32(control.Argument.Value));
                            if (control.Skip > 0)
                            {
                                i += control.Skip;
                            }
                        }
                    }

                    break;
                default:
                    if (!char.IsControl(ch))
                    {
                        builder.Append(ch);
                    }

                    i++;
                    break;
            }
        }

        return builder.ToString().Replace("\r\n\r\n", "\n\n").Trim();
    }

    private static (string? Word, int? Argument, int Skip) ReadControlWord(string rtf, ref int index)
    {
        var start = index;
        while (index < rtf.Length && char.IsLetter(rtf[index]))
        {
            index++;
        }

        var word = index > start ? rtf[start..index] : null;

        var negative = false;
        if (index < rtf.Length && rtf[index] == '-')
        {
            negative = true;
            index++;
        }

        var argStart = index;
        while (index < rtf.Length && char.IsDigit(rtf[index]))
        {
            index++;
        }

        int? argument = null;
        if (index > argStart)
        {
            var span = rtf[argStart..index];
            if (int.TryParse(span, out var value))
            {
                argument = negative ? -value : value;
            }
        }

        if (index < rtf.Length && rtf[index] == ' ')
        {
            index++;
        }

        return (word, argument, 0);
    }

    private static string? ExtractTitle(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
    }

        var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            return line.Length > 120 ? line[..120].Trim() : line;
        }

        return null;
    }

    private static RtfExtractionResult BuildExtraction(string text, StreamInfo streamInfo)
    {
        var segments = new List<DocumentSegment>();
        var artifacts = new ConversionArtifacts();
        var trimmed = TextSanitizer.Normalize(text, trim: true);

        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            var metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Page] = "1"
            };

            var segment = new DocumentSegment(
                markdown: trimmed,
                type: SegmentType.Page,
                number: 1,
                label: "Page 1",
                source: streamInfo.FileName,
                additionalMetadata: metadata);

            segments.Add(segment);
            artifacts.TextBlocks.Add(new TextArtifact(trimmed, 1, streamInfo.FileName, segment.Label));
        }

        return new RtfExtractionResult(segments, artifacts, trimmed);
    }

    private sealed class RtfExtractionResult
    {
        public RtfExtractionResult(List<DocumentSegment> segments, ConversionArtifacts artifacts, string rawText)
        {
            Segments = segments;
            Artifacts = artifacts;
            RawText = rawText;
        }

        public List<DocumentSegment> Segments { get; }

        public ConversionArtifacts Artifacts { get; }

        public string RawText { get; }
    }
}
