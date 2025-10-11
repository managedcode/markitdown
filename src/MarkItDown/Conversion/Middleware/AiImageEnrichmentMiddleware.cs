using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarkItDown.Conversion.Middleware;

/// <summary>
/// Middleware that enriches extracted images using an <see cref="IChatClient"/>.
/// </summary>
public sealed class AiImageEnrichmentMiddleware : IConversionMiddleware
{
    public async Task InvokeAsync(ConversionPipelineContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.Artifacts.Images.Count == 0)
        {
            return;
        }

        var chatClient = context.AiModels.ChatClient;
        if (chatClient is null)
        {
            return;
        }

        async Task ProcessImageAsync(ImageArtifact image)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (image.DetailedDescription is not null)
            {
                return;
            }

            var prompt = BuildPrompt(context.StreamInfo, image);
            ChatResponse<ImageInsight>? response = null;

            try
            {
                response = await chatClient.GetResponseAsync<ImageInsight>(
                    prompt,
                    new ChatOptions
                    {
                        Temperature = 0.1f,
                    },
                    useJsonSchemaResponseFormat: true,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                context.Logger?.LogWarning(ex, "Image enrichment failed for {Label}", image.Label ?? image.PageNumber?.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (response is null)
            {
                return;
            }

            ImageInsight? insight = null;
            if (!response.TryGetResult(out insight) && !string.IsNullOrWhiteSpace(response.Text))
            {
                try
                {
                    insight = JsonSerializer.Deserialize<ImageInsight>(response.Text);
                }
                catch (JsonException)
                {
                    insight = new ImageInsight { Summary = response.Text }; // fall back to raw text
                }
            }

            var markdown = insight?.ToMarkdown();
            if (string.IsNullOrWhiteSpace(markdown))
            {
                markdown = response.Text;
            }

            if (string.IsNullOrWhiteSpace(markdown))
            {
                return;
            }

            image.DetailedDescription = markdown.Trim();
            image.MermaidDiagram = insight?.MermaidDiagram;
            image.RawText = insight?.ExtractedText;

            UpdateImageMetadata(image, insight);
            UpdateSegments(context, image, markdown!, insight?.MermaidDiagram);

            image.Metadata["detailedDescription"] = image.DetailedDescription;
        }

        if (context.Artifacts.Images is List<ImageArtifact> list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                await ProcessImageAsync(list[i]).ConfigureAwait(false);
            }
        }
        else
        {
            foreach (var image in context.Artifacts.Images)
            {
                await ProcessImageAsync(image).ConfigureAwait(false);
            }
        }
    }

    private static string BuildPrompt(StreamInfo streamInfo, ImageArtifact image)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are an assistant that analyses embedded document images to produce exhaustive textual descriptions.");
        builder.AppendLine("Describe every meaningful component, explain relationships, and capture all numeric values.");
        builder.AppendLine("If the image contains a flow chart, architecture, timeline, UML, or graph, produce a Mermaid diagram representing it.");
        builder.AppendLine("If the content is a chart or table, provide a Markdown table capturing the data points.");
        builder.AppendLine("Always respond with JSON using the following schema:");
        builder.AppendLine("{\"summary\": string, \"keyFindings\": string[], \"mermaidDiagram\": string | null, \"dataTableMarkdown\": string | null, \"extractedText\": string | null}");
        builder.AppendLine("The summary should be a dense paragraph covering every notable element.");
        builder.AppendLine("Key findings should include facts, metrics, and contextual insights.");
        builder.AppendLine("When a Mermaid diagram is not applicable return null for mermaidDiagram.");
        builder.AppendLine("When there is no table or chart return null for dataTableMarkdown.");
        builder.AppendLine("When no OCR text is relevant return null for extractedText.");

        builder.AppendLine();
        builder.AppendLine("Image metadata:");
        var source = streamInfo.FileName ?? streamInfo.LocalPath ?? streamInfo.Url ?? "unknown";
        builder.AppendLine($"- Source: {source}");
        builder.AppendLine($"- Page: {image.PageNumber?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
        builder.AppendLine($"- MimeType: {image.ContentType ?? "unknown"}");
        builder.AppendLine();

        builder.Append("ImagePayload: data:");
        builder.Append(image.ContentType ?? "application/octet-stream");
        builder.Append(";base64,");

        var base64Length = checked(((image.Data.Length + 2) / 3) * 4);
        char[]? rented = null;
        Span<char> buffer = base64Length <= 4096
            ? stackalloc char[base64Length]
            : (rented = ArrayPool<char>.Shared.Rent(base64Length));

        try
        {
            if (Convert.TryToBase64Chars(image.Data, buffer, out var charsWritten))
            {
                builder.Append(buffer[..charsWritten]);
            }
            else
            {
                builder.Append(Convert.ToBase64String(image.Data));
            }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }

        return builder.ToString();
    }

    private static void UpdateImageMetadata(ImageArtifact image, ImageInsight? insight)
    {
        if (!string.IsNullOrWhiteSpace(insight?.Summary))
        {
            image.Metadata[MetadataKeys.Caption] = insight!.Summary!;
        }

        if (!string.IsNullOrWhiteSpace(insight?.ExtractedText))
        {
            image.Metadata["ocrText"] = insight!.ExtractedText!;
        }

        if (!string.IsNullOrWhiteSpace(insight?.DataTableMarkdown))
        {
            image.Metadata["dataTableMarkdown"] = insight!.DataTableMarkdown!;
        }
    }

    private static void UpdateSegments(ConversionPipelineContext context, ImageArtifact image, string markdown, string? mermaid)
    {
        if (image.SegmentIndex is int index && index >= 0 && index < context.Segments.Count)
        {
            var existing = context.Segments[index];
            var metadata = new Dictionary<string, string>(existing.AdditionalMetadata)
            {
                ["imageEnriched"] = "true"
            };

            if (image.PageNumber is int page && !metadata.ContainsKey(MetadataKeys.Page))
            {
                metadata[MetadataKeys.Page] = page.ToString(CultureInfo.InvariantCulture);
            }

            var enrichmentBlock = BuildEnrichmentBlock(markdown, mermaid);
            var updatedMarkdown = existing.Markdown;

            if (!string.IsNullOrWhiteSpace(image.PlaceholderMarkdown) &&
                TryInjectAfterPlaceholder(existing.Markdown, image.PlaceholderMarkdown!, enrichmentBlock, out var injected))
            {
                updatedMarkdown = injected;
            }
            else
            {
                var builder = new StringBuilder(existing.Markdown);
                builder.Append(enrichmentBlock);
                updatedMarkdown = builder.ToString();
            }

            context.Segments[index] = new DocumentSegment(
                updatedMarkdown.TrimEnd(),
                existing.Type,
                existing.Number,
                existing.Label,
                existing.StartTime,
                existing.EndTime,
                existing.Source,
                metadata);
        }
        else
        {
            var metadata = new Dictionary<string, string>
            {
                ["imageEnriched"] = "true"
            };

            if (image.PageNumber is int page)
            {
                metadata[MetadataKeys.Page] = page.ToString(CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrWhiteSpace(mermaid))
            {
                metadata["mermaid"] = "true";
            }

            var composed = BuildStandaloneEnrichment(markdown, mermaid);

            context.Segments.Add(new DocumentSegment(
                composed,
                SegmentType.Image,
                image.PageNumber,
                image.Label,
                source: image.Source,
                additionalMetadata: metadata));
            image.SegmentIndex = context.Segments.Count - 1;
            image.PlaceholderMarkdown ??= markdown.Trim();
        }
    }

    private static bool ContainsMermaid(string markdown)
        => markdown.Contains("```mermaid", StringComparison.OrdinalIgnoreCase);

    private static string BuildEnrichmentBlock(string markdown, string? mermaid)
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine(markdown.Trim());

        if (!string.IsNullOrWhiteSpace(mermaid) && !ContainsMermaid(markdown))
        {
            builder.AppendLine();
            builder.AppendLine("```mermaid");
            builder.AppendLine(mermaid!.Trim());
            builder.AppendLine("```");
        }

        return builder.ToString();
    }

    private static string BuildStandaloneEnrichment(string markdown, string? mermaid)
    {
        var builder = new StringBuilder(markdown.Trim());

        if (!string.IsNullOrWhiteSpace(mermaid) && !ContainsMermaid(markdown))
        {
            builder.AppendLine().AppendLine("```mermaid");
            builder.AppendLine(mermaid!.Trim());
            builder.AppendLine("```");
        }

        return builder.ToString().TrimEnd();
    }

    private static bool TryInjectAfterPlaceholder(string existingMarkdown, string placeholder, string enrichmentBlock, out string updated)
    {
        var index = existingMarkdown.IndexOf(placeholder, StringComparison.Ordinal);
        if (index < 0)
        {
            updated = existingMarkdown;
            return false;
        }

        var insertPosition = index + placeholder.Length;
        var builder = new StringBuilder(existingMarkdown.Length + enrichmentBlock.Length);
        builder.Append(existingMarkdown, 0, insertPosition);
        builder.Append(enrichmentBlock);
        builder.Append(existingMarkdown.AsSpan(insertPosition));
        updated = builder.ToString();
        return true;
    }

    private sealed class ImageInsight
    {
        public string? Summary { get; set; }

        public IList<string> KeyFindings { get; set; } = new List<string>();

        public string? MermaidDiagram { get; set; }

        public string? DataTableMarkdown { get; set; }

        public string? ExtractedText { get; set; }

        public string ToMarkdown()
        {
            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(Summary))
            {
                builder.AppendLine(Summary.Trim());
            }

            if (KeyFindings.Count > 0)
            {
                var originalLength = builder.Length;
                var anyFinding = false;
                builder.AppendLine().AppendLine("Key findings:");

                void AppendFinding(string? finding)
                {
                    if (string.IsNullOrWhiteSpace(finding))
                    {
                        return;
                    }

                    builder.Append("- ").AppendLine(finding.Trim());
                    anyFinding = true;
                }

                if (KeyFindings is List<string> list)
                {
                    foreach (var finding in CollectionsMarshal.AsSpan(list))
                    {
                        AppendFinding(finding);
                    }
                }
                else
                {
                    foreach (var finding in KeyFindings)
                    {
                        AppendFinding(finding);
                    }
                }

                if (!anyFinding)
                {
                    builder.Length = originalLength;
                }
            }

            if (!string.IsNullOrWhiteSpace(DataTableMarkdown))
            {
                builder.AppendLine().AppendLine(DataTableMarkdown.Trim());
            }

            if (!string.IsNullOrWhiteSpace(ExtractedText))
            {
                builder.AppendLine().AppendLine("Extracted text:");
                builder.AppendLine(ExtractedText.Trim());
            }

            return builder.ToString().Trim();
        }
    }
}
