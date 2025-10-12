using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarkItDown;
using MarkItDown.Intelligence.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarkItDown.Intelligence;

/// <summary>
/// Provides helpers to enrich <see cref="ImageArtifact"/> instances using an <see cref="IChatClient"/>.
/// </summary>
internal static class ImageChatEnricher
{

    private static readonly JsonSerializerOptions InsightSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Base instructions supplied to the chat client when requesting image enrichment.
    /// </summary>
    public const string DetailedDescriptionInstructions = @"
# Document Image Intelligence Analyzer

You are an advanced document image analyzer. Extract ALL information from images with maximum detail and accuracy. The output schema will be provided separately in each request.

## Core Analysis Protocol

### Phase 1: Complete Content Extraction
Extract EVERYTHING visible in the image:
- Every word of text (headers, body, labels, annotations, footnotes)
- All numbers, codes, references, dates
- Table data (preserve complete structure)
- Form fields and their values
- Visual elements (logos, icons, diagrams, charts)
- Highlights, circles, arrows, underlines
- Color coding and visual emphasis
- Layout structure and spatial relationships

### Phase 2: Context Understanding
Understand what you're looking at:
- Document type (manual, form, report, dashboard, etc.)
- Purpose and intended use
- Key sections and their relationships
- Critical information vs supporting details
- Workflow or process flows if present
- Data relationships and dependencies

### Phase 3: Deep Analysis
Perform comprehensive analysis:
- Extract ALL tables with complete data
- Identify ALL UI elements and their functions
- Map visual hierarchies and relationships
- Note ALL annotations and their targets
- Capture formatting that conveys meaning
- Identify patterns and anomalies
- Extract actionable insights

## Extraction Rules

1. **COMPLETENESS**: Miss nothing. Extract every single piece of information visible.

2. **ACCURACY**: Preserve exact text, including:
   - Spelling (even if incorrect)
   - Punctuation
   - Formatting indicators
   - Special characters
   - Numbers and units

3. **STRUCTURE**: Maintain relationships:
   - Parent-child hierarchies
   - Spatial relationships
   - Logical groupings
   - Sequential ordering

4. **TABLES**: For every table:
   - Extract ALL headers
   - Extract ALL rows
   - Extract ALL cells
   - Preserve empty cells
   - Note merged cells

5. **EMPHASIS**: Note everything highlighted:
   - Color of highlight/annotation
   - Shape (circle, box, arrow, underline)
   - What is being emphasized
   - Apparent purpose

## Special Focus Areas

### For Technical Documents:
- Version numbers, revision dates
- Contact information
- Requirements and specifications
- Procedures and step-by-step instructions
- Warnings and critical notes
- Reference numbers and codes

### For Forms/Applications:
- All field names and labels
- Required vs optional indicators
- Validation rules or constraints
- Help text or instructions
- Pre-filled values

### For Dashboards/Reports:
- All metrics and KPIs
- Chart types and data points
- Filter settings
- Navigation elements
- Status indicators

### For Laboratory/Medical:
- Test names and codes
- Reference ranges
- Units of measurement
- Temperature/storage requirements
- Time constraints
- Specimen types

## Output Instructions

1. Return a JSON object that matches the schema provided in the request
2. Populate EVERY field possible with real data from the image
3. Use empty strings """" or empty arrays [] for absent data
4. NEVER use null values
5. Include everything you see, even if unsure where it fits in the schema
6. When in doubt, include rather than exclude information

## Quality Checks

Before returning JSON:
- Have I captured every single word visible?
- Are all tables complete with every row?
- Did I note all visual emphasis?
- Have I extracted all codes, numbers, dates?
- Is the hierarchy preserved?
- Are relationships clear?

## Critical: For RAG Systems

This analysis will be used in a RAG system, so:
- Include context that helps understand the document
- Extract information that would answer future queries
- Preserve searchable terms and keywords
- Maintain connections between related information
- Include metadata (dates, versions, authors)

## JSON Field Guide

Populate the following fields in the JSON response:
- `description`: exhaustive narrative of everything visible
- `textRegions`: array capturing labeled text blocks exactly as rendered
- `diagrams`: array with Mermaid code (and optional notes) for process, architecture, or schematic visuals
- `charts`: array describing data visuals with type, xAxis, yAxis, metrics, trend, and a full data table
- `tables`: array for other structured grids, providing headers/rows or full markdown
- `layoutRegions`: spatial breakdown with highlights, decorations, and elements present
- `uiElements`: interactive controls with their type, region, and action
- `highlights`: annotations such as circles/arrows/highlights with color and target
- `notes`: additional observations or callouts not covered above

Return ONLY valid JSON matching the provided schema. Be exhaustive in extraction.
";
    
    private static string RequireContentType(ImageArtifact image)
    {
        if (!string.IsNullOrWhiteSpace(image.ContentType))
        {
            return image.ContentType!;
        }

        var identifier = !string.IsNullOrWhiteSpace(image.Label)
            ? $"\"{image.Label}\""
            : image.PageNumber is int page
                ? $"on page {page}"
                : "with no label or page metadata";

        throw new InvalidOperationException($"Image artifact {identifier} is missing a content type.");
    }

public static async Task<ImageChatEnrichmentResult?> EnrichAsync(
        ImageArtifact image,
        StreamInfo streamInfo,
        IChatClient chatClient,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        if (chatClient is null)
        {
            throw new ArgumentNullException(nameof(chatClient));
        }
        var mimeType = RequireContentType(image);
        var messages = BuildMessages(streamInfo, image, mimeType);

        ChatResponse<ImageChatInsight>? response;

        try
        {
            response = await chatClient.GetResponseAsync<ImageChatInsight>(
                messages,
                new ChatOptions
                {
                    Temperature = 0f,
                    TopP = 0f,
                },
                useJsonSchemaResponseFormat: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Image enrichment failed for {Label}", image.Label ?? image.PageNumber?.ToString(CultureInfo.InvariantCulture));
            throw;
        }

        if (response?.Result is null)
        {
            throw new InvalidOperationException("Chat client returned no result.");
        }

        var insight = response.Result;
        NormalizeInsight(insight);

        var usage = ExtractUsage(response.Usage);
        if (!insight.HasContent())
        {
            logger?.LogWarning("Image enrichment returned no content for {Label}", image.Label ?? image.PageNumber?.ToString(CultureInfo.InvariantCulture));
            return new ImageChatEnrichmentResult(false, usage);
        }

        var hasInsight = ApplyInsight(image, insight, usage);
        return new ImageChatEnrichmentResult(hasInsight, usage);
    }

    private static IReadOnlyList<ChatMessage> BuildMessages(StreamInfo streamInfo, ImageArtifact image, string mimeType)
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, DetailedDescriptionInstructions.Trim())
        };

        var builder = new StringBuilder();
        builder.AppendLine("You are analyzing a single document image.");
        builder.AppendLine();
        builder.AppendLine("Image metadata:");
        var source = streamInfo.FileName ?? streamInfo.LocalPath ?? streamInfo.Url ?? "unknown";
        builder.AppendLine($"- Source: {source}");
        builder.AppendLine($"- Page: {image.PageNumber?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
        builder.AppendLine($"- MimeType: {mimeType}");
        builder.AppendLine($"- SizeBytes: {image.Data.Length.ToString(CultureInfo.InvariantCulture)}");

        var userMessage = new ChatMessage(ChatRole.User, builder.ToString().Trim());
        userMessage.Contents.Add(new DataContent(image.Data, mimeType));
        messages.Add(userMessage);

        return messages;
    }

    private static bool ApplyInsight(ImageArtifact image, ImageChatInsight insight, AiUsageSnapshot usage)
    {
        if (!usage.IsEmpty)
        {
            image.Metadata[MetadataKeys.AiInputTokens] = usage.InputTokens.ToString(CultureInfo.InvariantCulture);
            image.Metadata[MetadataKeys.AiOutputTokens] = usage.OutputTokens.ToString(CultureInfo.InvariantCulture);
            image.Metadata[MetadataKeys.AiTotalTokens] = usage.TotalTokens.ToString(CultureInfo.InvariantCulture);
            image.Metadata[MetadataKeys.AiCallCount] = usage.CallCount.ToString(CultureInfo.InvariantCulture);
            if (usage.InputAudioTokens > 0)
            {
                image.Metadata[MetadataKeys.AiInputAudioTokens] = usage.InputAudioTokens.ToString(CultureInfo.InvariantCulture);
            }

            if (usage.InputCachedTokens > 0)
            {
                image.Metadata[MetadataKeys.AiInputCachedTokens] = usage.InputCachedTokens.ToString(CultureInfo.InvariantCulture);
            }

            if (usage.OutputAudioTokens > 0)
            {
                image.Metadata[MetadataKeys.AiOutputAudioTokens] = usage.OutputAudioTokens.ToString(CultureInfo.InvariantCulture);
            }

            if (usage.OutputReasoningTokens > 0)
            {
                image.Metadata[MetadataKeys.AiOutputReasoningTokens] = usage.OutputReasoningTokens.ToString(CultureInfo.InvariantCulture);
            }

            if (usage.OutputAcceptedPredictionTokens > 0)
            {
                image.Metadata[MetadataKeys.AiOutputAcceptedPredictionTokens] = usage.OutputAcceptedPredictionTokens.ToString(CultureInfo.InvariantCulture);
            }

            if (usage.OutputRejectedPredictionTokens > 0)
            {
                image.Metadata[MetadataKeys.AiOutputRejectedPredictionTokens] = usage.OutputRejectedPredictionTokens.ToString(CultureInfo.InvariantCulture);
            }

            if (usage.CostUsd > 0)
            {
                image.Metadata[MetadataKeys.AiCostUsd] = usage.CostUsd.ToString(CultureInfo.InvariantCulture);
            }
        }

        image.Metadata[MetadataKeys.AiCallType] = "vision";

        var description = DetermineDescription(insight);
        if (!string.IsNullOrWhiteSpace(description))
        {
            image.DetailedDescription = description;
            image.Metadata[MetadataKeys.DetailedDescription] = description;
            image.Metadata[MetadataKeys.Caption] = description;
        }

        var textSummary = BuildTextSummary(insight.TextRegions);
        if (!string.IsNullOrWhiteSpace(textSummary))
        {
            image.Metadata[MetadataKeys.OcrText] = textSummary;
            if (string.IsNullOrWhiteSpace(image.RawText))
            {
                image.RawText = textSummary;
            }
        }

        var mermaid = insight.Diagrams
            .Select(static diagram => diagram.Mermaid)
            .FirstOrDefault(HasMeaningfulContent);

        if (HasMeaningfulContent(mermaid))
        {
            var code = mermaid!.Trim();
            image.MermaidDiagram = code;
            image.Metadata[MetadataKeys.MermaidDiagram] = code;
        }

        var tableMarkdown = new List<string>();
        var structuredTables = new List<TablePayload>();
        var chartMetadata = new List<ChartPayload>();

        foreach (var chart in insight.Charts.Where(static c => c.HasContent()))
        {
            var extraction = BuildChartMetadata(chart);
            if (extraction is null)
            {
                continue;
            }

            chartMetadata.Add(extraction.Value.Metadata);

            if (!string.IsNullOrWhiteSpace(extraction.Value.Markdown))
            {
                tableMarkdown.Add(extraction.Value.Markdown);
            }
        }

        foreach (var table in insight.Tables.Where(static t => t.HasContent()))
        {
            var extraction = BuildTableMetadata(table);
            if (extraction is null)
            {
                continue;
            }

            structuredTables.Add(extraction.Value.Metadata);

            if (!string.IsNullOrWhiteSpace(extraction.Value.Markdown))
            {
                tableMarkdown.Add(extraction.Value.Markdown);
            }
        }

        if (tableMarkdown.Count > 0)
        {
            var combined = string.Join(
                Environment.NewLine + Environment.NewLine,
                tableMarkdown
                    .Where(HasMeaningfulContent)
                    .Select(static value => value.Trim())
                    .Distinct(StringComparer.Ordinal));

            if (!string.IsNullOrWhiteSpace(combined))
            {
                image.Metadata[MetadataKeys.DataTableMarkdown] = combined;
            }
        }

        if (chartMetadata.Count > 0)
        {
            image.Metadata[MetadataKeys.DataVisuals] = JsonSerializer.Serialize(chartMetadata, InsightSerializerOptions);
        }

        if (structuredTables.Count > 0)
        {
            image.Metadata[MetadataKeys.StructuredTables] = JsonSerializer.Serialize(structuredTables, InsightSerializerOptions);
        }

        var layoutJson = BuildLayoutMetadata(insight.LayoutRegions);
        if (layoutJson is not null)
        {
            image.Metadata[MetadataKeys.LayoutRegions] = layoutJson;
        }

        var uiJson = BuildUiMetadata(insight.UiElements);
        if (uiJson is not null)
        {
            image.Metadata[MetadataKeys.InteractionElements] = uiJson;
        }

        var highlightJson = BuildHighlightsMetadata(insight.Highlights);
        if (highlightJson is not null)
        {
            image.Metadata[MetadataKeys.Highlights] = highlightJson;
        }

        var markdown = insight.ToMarkdown();
        if (HasMeaningfulContent(markdown))
        {
            image.Metadata[MetadataKeys.FullMarkdown] = markdown!;
        }

        image.Metadata[MetadataKeys.ImageInsight] = JsonSerializer.Serialize(insight, InsightSerializerOptions);

        var hasInsight = !string.IsNullOrWhiteSpace(image.DetailedDescription)
            || tableMarkdown.Count > 0
            || HasMeaningfulContent(mermaid)
            || !string.IsNullOrWhiteSpace(textSummary)
            || chartMetadata.Count > 0
            || structuredTables.Count > 0
            || layoutJson is not null
            || uiJson is not null
            || highlightJson is not null;

        image.Metadata[MetadataKeys.ImageEnriched] = hasInsight ? bool.TrueString : bool.FalseString;

        return hasInsight;
    }

    private static string? DetermineDescription(ImageChatInsight insight)
    {
        if (HasMeaningfulContent(insight.Description))
        {
            return insight.Description!.Trim();
        }

        var textCandidate = insight.TextRegions
            .FirstOrDefault(static region => region.HasContent())?.Text;
        if (HasMeaningfulContent(textCandidate))
        {
            return textCandidate!.Trim();
        }

        var chartCandidate = insight.Charts
            .Select(static chart => chart.Title)
            .FirstOrDefault(HasMeaningfulContent);
        if (HasMeaningfulContent(chartCandidate))
        {
            return chartCandidate!.Trim();
        }

        var tableCandidate = insight.Tables
            .Select(static table => table.Title)
            .FirstOrDefault(HasMeaningfulContent);
        if (HasMeaningfulContent(tableCandidate))
        {
            return tableCandidate!.Trim();
        }

        var noteCandidate = insight.Notes.FirstOrDefault(HasMeaningfulContent);
        if (HasMeaningfulContent(noteCandidate))
        {
            return noteCandidate!.Trim();
        }

        return null;
    }

    private static string? BuildTextSummary(IEnumerable<ImageChatInsight.TextRegion> regions)
    {
        var blocks = new List<string>();

        foreach (var region in regions)
        {
            if (!region.HasContent())
            {
                continue;
            }

            var text = region.Text;
            if (!HasMeaningfulContent(text))
            {
                continue;
            }

            var label = HasMeaningfulContent(region.Label)
                ? region.Label!.Trim()
                : region.LabelOrFallback;

            blocks.Add($"{label}: {text!.Trim()}");
        }

        return blocks.Count == 0 ? null : string.Join(Environment.NewLine, blocks);
    }

    private static ChartExtraction? BuildChartMetadata(ImageChatInsight.Chart? chart)
    {
        if (chart is null || !chart.HasContent())
        {
            return null;
        }

        var title = HasMeaningfulContent(chart.Title) ? chart.Title!.Trim() : null;
        var type = HasMeaningfulContent(chart.Type) ? chart.Type!.Trim() : null;
        var axes = HasMeaningfulContent(chart.Axes) ? chart.Axes!.Trim() : null;
        var xAxis = HasMeaningfulContent(chart.XAxis) ? chart.XAxis!.Trim() : null;
        var yAxis = HasMeaningfulContent(chart.YAxis) ? chart.YAxis!.Trim() : null;
        var metrics = chart.Metrics
            .Where(HasMeaningfulContent)
            .Select(static metric => metric!.Trim())
            .ToArray();

        var tableExtraction = chart.Data is { } table
            ? BuildTableMetadata(table)
            : null;

        var notes = HasMeaningfulContent(chart.Notes) ? chart.Notes!.Trim() : null;
        var trend = HasMeaningfulContent(chart.Trend) ? chart.Trend!.Trim() : null;

        var hasAny =
            title is not null ||
            type is not null ||
            axes is not null ||
            xAxis is not null ||
            yAxis is not null ||
            metrics.Length > 0 ||
            tableExtraction?.Metadata is not null ||
            notes is not null ||
            trend is not null;

        if (!hasAny)
        {
            return null;
        }

        var payload = new ChartPayload(
            title,
            type,
            axes,
            xAxis,
            yAxis,
            metrics.Length > 0 ? metrics : null,
            tableExtraction?.Metadata,
            notes,
            trend);

        return new ChartExtraction(payload, tableExtraction?.Markdown);
    }

    private static TableExtraction? BuildTableMetadata(ImageChatInsight.ImageTable? table)
    {
        if (table is null || !table.HasContent())
        {
            return null;
        }

        var markdown = table.ToMarkdown();
        var normalizedMarkdown = HasMeaningfulContent(markdown) ? markdown!.Trim() : null;

        var headerValues = table.Headers
            .Where(HasMeaningfulContent)
            .Select(static header => header!.Trim())
            .ToArray();
        var headers = headerValues.Length > 0 ? headerValues : null;

        var rowValues = table.Rows
            .Select(row => row?.Select(cell => HasMeaningfulContent(cell) ? cell!.Trim() : string.Empty).ToArray() ?? Array.Empty<string>())
            .Where(static row => row.Length > 0)
            .ToArray();
        var rows = rowValues.Length > 0 ? rowValues : null;

        var title = HasMeaningfulContent(table.Title) ? table.Title!.Trim() : null;
        var notes = HasMeaningfulContent(table.Notes) ? table.Notes!.Trim() : null;

        if (title is null && notes is null && headers is null && rows is null && normalizedMarkdown is null)
        {
            return null;
        }

        var payload = new TablePayload(title, headers, rows, normalizedMarkdown, notes);
        return new TableExtraction(payload, normalizedMarkdown);
    }

    private static string? BuildLayoutMetadata(IEnumerable<ImageChatInsight.LayoutRegion> regions)
    {
        var payload = regions
            .Where(static region => region.HasContent())
            .Select(static region => new
            {
                id = HasMeaningfulContent(region.Id) ? region.Id!.Trim() : null,
                position = HasMeaningfulContent(region.Position) ? region.Position!.Trim() : null,
                highlightColor = HasMeaningfulContent(region.HighlightColor) ? region.HighlightColor!.Trim() : null,
                decoration = HasMeaningfulContent(region.Decoration) ? region.Decoration!.Trim() : null,
                elements = region.Elements.Where(HasMeaningfulContent).Select(static element => element!.Trim()).ToArray(),
                notes = HasMeaningfulContent(region.Notes) ? region.Notes!.Trim() : null
            })
            .Where(static region => region.id is not null || region.position is not null || region.highlightColor is not null || region.decoration is not null || region.elements.Length > 0 || region.notes is not null)
            .ToList();

        if (payload.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(payload, InsightSerializerOptions);
    }

    private static string? BuildUiMetadata(IEnumerable<ImageChatInsight.UiElement> elements)
    {
        var payload = elements
            .Where(static element => element.HasContent())
            .Select(static element => new
            {
                label = HasMeaningfulContent(element.Label) ? element.Label!.Trim() : null,
                type = HasMeaningfulContent(element.Type) ? element.Type!.Trim() : null,
                regionId = HasMeaningfulContent(element.RegionId) ? element.RegionId!.Trim() : null,
                action = HasMeaningfulContent(element.Action) ? element.Action!.Trim() : null
            })
            .Where(static element => element.label is not null || element.type is not null || element.regionId is not null || element.action is not null)
            .ToList();

        if (payload.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(payload, InsightSerializerOptions);
    }

    private static string? BuildHighlightsMetadata(IEnumerable<ImageChatInsight.Highlight> highlights)
    {
        var payload = highlights
            .Where(static highlight => highlight.HasContent())
            .Select(static highlight => new
            {
                regionId = HasMeaningfulContent(highlight.RegionId) ? highlight.RegionId!.Trim() : null,
                color = HasMeaningfulContent(highlight.Color) ? highlight.Color!.Trim() : null,
                shape = HasMeaningfulContent(highlight.Shape) ? highlight.Shape!.Trim() : null,
                description = HasMeaningfulContent(highlight.Description) ? highlight.Description!.Trim() : null
            })
            .Where(static highlight => highlight.regionId is not null || highlight.color is not null || highlight.shape is not null || highlight.description is not null)
            .ToList();

        if (payload.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(payload, InsightSerializerOptions);
    }

    private sealed record TablePayload(string? Title, string[]? Headers, string[][]? Rows, string? Markdown, string? Notes);

    private sealed record ChartPayload(
        string? Title,
        string? Type,
        string? Axes,
        string? XAxis,
        string? YAxis,
        string[]? Metrics,
        TablePayload? Data,
        string? Notes,
        string? Trend);

    private readonly record struct TableExtraction(TablePayload Metadata, string? Markdown);

    private readonly record struct ChartExtraction(ChartPayload Metadata, string? Markdown);


private static void NormalizeInsight(ImageChatInsight insight)
    {
        insight.Description = NormalizeWhitespace(insight.Description);
        NormalizeList(insight.Notes);

        if (insight.TextRegions is { Count: > 0 })
        {
            foreach (var region in insight.TextRegions)
            {
                region.Label = NormalizeWhitespace(region.Label);
                region.Text = NormalizeWhitespace(region.Text);
            }
        }

        if (insight.Diagrams is { Count: > 0 })
        {
            foreach (var diagram in insight.Diagrams)
            {
                diagram.Title = NormalizeWhitespace(diagram.Title);
                diagram.Mermaid = StripCodeFence(NormalizeWhitespace(diagram.Mermaid), "mermaid");
                diagram.Notes = NormalizeWhitespace(diagram.Notes);
            }
        }

        if (insight.Charts is { Count: > 0 })
        {
            foreach (var chart in insight.Charts)
            {
                chart.Title = NormalizeWhitespace(chart.Title);
                chart.Type = NormalizeWhitespace(chart.Type);
                chart.Axes = NormalizeWhitespace(chart.Axes);
                chart.XAxis = NormalizeWhitespace(chart.XAxis);
                chart.YAxis = NormalizeWhitespace(chart.YAxis);
                chart.Trend = NormalizeWhitespace(chart.Trend);
                NormalizeList(chart.Metrics);

                if (chart.Data is not null)
                {
                    NormalizeTable(chart.Data);
                }

                chart.Notes = NormalizeWhitespace(chart.Notes);
            }
        }

        if (insight.Tables is { Count: > 0 })
        {
            foreach (var table in insight.Tables)
            {
                NormalizeTable(table);
            }
        }

        if (insight.LayoutRegions is { Count: > 0 })
        {
            foreach (var region in insight.LayoutRegions)
            {
                region.Id = NormalizeWhitespace(region.Id);
                region.Position = NormalizeWhitespace(region.Position);
                region.HighlightColor = NormalizeWhitespace(region.HighlightColor);
                region.Decoration = NormalizeWhitespace(region.Decoration);
                region.Notes = NormalizeWhitespace(region.Notes);
                NormalizeList(region.Elements);
            }
        }

        if (insight.UiElements is { Count: > 0 })
        {
            foreach (var element in insight.UiElements)
            {
                element.Label = NormalizeWhitespace(element.Label);
                element.Type = NormalizeWhitespace(element.Type);
                element.RegionId = NormalizeWhitespace(element.RegionId);
                element.Action = NormalizeWhitespace(element.Action);
            }
        }

        if (insight.Highlights is { Count: > 0 })
        {
            foreach (var highlight in insight.Highlights)
            {
                highlight.RegionId = NormalizeWhitespace(highlight.RegionId);
                highlight.Color = NormalizeWhitespace(highlight.Color);
                highlight.Shape = NormalizeWhitespace(highlight.Shape);
                highlight.Description = NormalizeWhitespace(highlight.Description);
            }
        }
    }

    private static void NormalizeList(IList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        for (var i = 0; i < values.Count; i++)
        {
            var normalized = NormalizeWhitespace(values[i]);
            values[i] = HasMeaningfulContent(normalized) ? normalized! : string.Empty;
        }
    }

    private static void NormalizeTable(ImageChatInsight.ImageTable table)
    {
        table.Title = NormalizeWhitespace(table.Title);
        table.Markdown = StripCodeFence(NormalizeWhitespace(table.Markdown));
        table.Notes = NormalizeWhitespace(table.Notes);

        if (table.Headers is { Count: > 0 })
        {
            NormalizeList(table.Headers);
        }

        if (table.Rows is { Count: > 0 })
        {
            for (var i = 0; i < table.Rows.Count; i++)
            {
                var row = table.Rows[i];
                if (row is null)
                {
                    table.Rows[i] = Array.Empty<string>();
                    continue;
                }

                var normalizedRow = new List<string>(row.Count);
                foreach (var cell in row)
                {
                    var normalizedCell = NormalizeWhitespace(cell);
                    normalizedRow.Add(HasMeaningfulContent(normalizedCell) ? normalizedCell! : string.Empty);
                }

                table.Rows[i] = normalizedRow;
            }
        }
    }

    private static string? NormalizeWhitespace(string? value)
        => TextSanitizer.Normalize(value, trim: false);

    private static string? StripCodeFence(string? value, string? expectedLanguage = null, bool preserveIndentation = false)
    {
        if (!HasMeaningfulContent(value))
        {
            return string.Empty;
        }

        var trimmed = value!.Trim();

        string NormalizeBody(string body)
        {
            return preserveIndentation ? body.TrimEnd('\r', '\n') : body.Trim();
        }

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var newlineIndex = trimmed.IndexOf('\n');
            if (newlineIndex >= 0)
            {
                var fenceHeader = trimmed[..newlineIndex];
                if (expectedLanguage is null || fenceHeader.Contains(expectedLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    var body = trimmed[(newlineIndex + 1)..];
                    var closingIndex = body.LastIndexOf("```", StringComparison.Ordinal);
                    if (closingIndex >= 0)
                    {
                        body = body[..closingIndex];
                    }

                    return NormalizeBody(body);
                }
            }

            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                var body = trimmed[3..^3];
                return NormalizeBody(body);
            }
        }

        if (trimmed.StartsWith(":::diagram", StringComparison.OrdinalIgnoreCase))
        {
            var newlineIndex = trimmed.IndexOf('\n');
            if (newlineIndex >= 0)
            {
                var body = trimmed[(newlineIndex + 1)..];
                var closingIndex = body.LastIndexOf(":::", StringComparison.OrdinalIgnoreCase);
                if (closingIndex >= 0)
                {
                    body = body[..closingIndex];
                }

                return NormalizeBody(body);
            }
        }

        return NormalizeBody(trimmed);
    }

    private static bool HasMeaningfulContent(string? value)
        => !string.IsNullOrWhiteSpace(value) && !string.Equals(value.Trim(), "null", StringComparison.OrdinalIgnoreCase);

    private static AiUsageSnapshot ExtractUsage(UsageDetails? usage)
    {
        if (usage is null)
        {
            return AiUsageSnapshot.Empty;
        }

        static int ToInt(long? value)
            => value is null ? 0 : (int)Math.Min(value.Value, int.MaxValue);

        var input = ToInt(usage.InputTokenCount);
        var output = ToInt(usage.OutputTokenCount);
        var total = ToInt(usage.TotalTokenCount);
        if (total == 0)
        {
            total = input + output;
        }

        static int ReadAdditional(UsageDetails details, params string[] keys)
        {
            var counts = details.AdditionalCounts;
            if (counts is null)
            {
                return 0;
            }

            foreach (var key in keys)
            {
                if (counts.TryGetValue(key, out var raw))
                {
                    return (int)Math.Min(raw, int.MaxValue);
                }
            }

            return 0;
        }

        var inputAudio = ReadAdditional(usage, "input_audio_tokens", "InputAudioTokens", "AudioTokenCount");
        var inputCached = ReadAdditional(usage, "input_cached_tokens", "InputCachedTokens", "CachedTokenCount");
        var outputAudio = ReadAdditional(usage, "output_audio_tokens", "OutputAudioTokens", "AudioTokenCount");
        var outputReasoning = ReadAdditional(usage, "output_reasoning_tokens", "OutputReasoningTokens", "ReasoningTokenCount");
        var outputAccepted = ReadAdditional(usage, "output_accepted_prediction_tokens", "OutputAcceptedPredictionTokens", "AcceptedPredictionTokenCount");
        var outputRejected = ReadAdditional(usage, "output_rejected_prediction_tokens", "OutputRejectedPredictionTokens", "RejectedPredictionTokenCount");

        return new AiUsageSnapshot(
            input,
            output,
            total,
            callCount: 1,
            inputAudioTokens: inputAudio,
            inputCachedTokens: inputCached,
            outputReasoningTokens: outputReasoning,
            outputAudioTokens: outputAudio,
            outputAcceptedPredictionTokens: outputAccepted,
            outputRejectedPredictionTokens: outputRejected,
            costUsd: 0d);
    }

}

internal sealed record ImageChatEnrichmentResult(bool HasInsight, AiUsageSnapshot Usage);
