using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MarkItDown;

internal static class MetaMarkdownFormatter
{
    public static string? BuildImageComment(ImageArtifact artifact)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!-- Image description:");

        var hasContent = false;
        var description = ChooseDescription(artifact);
        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.AppendLine(description.Trim());
            builder.AppendLine();
            hasContent = true;
        }

        hasContent |= AppendListSection(builder, "Visible text", ExtractVisibleText(artifact));
        hasContent |= AppendLayoutSection(builder, artifact.Metadata.TryGetValue(MetadataKeys.LayoutRegions, out var layoutJson) ? layoutJson : null);
        hasContent |= AppendUiSection(builder, artifact.Metadata.TryGetValue(MetadataKeys.InteractionElements, out var uiJson) ? uiJson : null);
        hasContent |= AppendHighlightsSection(builder, artifact.Metadata.TryGetValue(MetadataKeys.Highlights, out var highlightsJson) ? highlightsJson : null);
        hasContent |= AppendDataVisualsSection(builder, artifact.Metadata.TryGetValue(MetadataKeys.DataVisuals, out var visualsJson) ? visualsJson : null);
        hasContent |= AppendTablesSection(builder, artifact);
        hasContent |= AppendAsciiArt(builder, artifact.Metadata.TryGetValue(MetadataKeys.AsciiArt, out var asciiArt) ? asciiArt : null);
        hasContent |= AppendMermaid(builder, artifact);

        if (!hasContent)
        {
            return null;
        }

        TrimTrailingBlankLines(builder);
        builder.Append("-->");
        return builder.ToString();
    }

    public static string? BuildDocumentMetadataComment(IDictionary<string, string> metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        var ordered = metadata
            .Where(static kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
            .OrderBy(static kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ordered.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine("<!-- Document metadata:");
        foreach (var kvp in ordered)
        {
            builder.AppendLine($"{kvp.Key.Trim()}: {kvp.Value.Trim()}");
        }

        builder.Append("-->");
        return builder.ToString();
    }

    private static string? ChooseDescription(ImageArtifact artifact)
    {
        if (!string.IsNullOrWhiteSpace(artifact.DetailedDescription))
        {
            return artifact.DetailedDescription;
        }

        if (artifact.Metadata.TryGetValue(MetadataKeys.DetailedDescription, out var detailed) && !string.IsNullOrWhiteSpace(detailed))
        {
            return detailed;
        }

        if (!string.IsNullOrWhiteSpace(artifact.Metadata.TryGetValue(MetadataKeys.Caption, out var caption) ? caption : artifact.Label))
        {
            return caption ?? artifact.Label;
        }

        if (artifact.Metadata.TryGetValue(MetadataKeys.FullMarkdown, out var markdown) && !string.IsNullOrWhiteSpace(markdown))
        {
            return markdown;
        }

        if (!string.IsNullOrWhiteSpace(artifact.RawText))
        {
            return artifact.RawText;
        }

        var location = DescribeLocation(artifact);
        if (!string.IsNullOrWhiteSpace(location))
        {
            return $"Image located on {location}.";
        }

        return "Image artifact captured without additional enrichment.";
    }

    private static string? DescribeLocation(ImageArtifact artifact)
    {
        if (artifact.PageNumber.HasValue)
        {
            return $"page {artifact.PageNumber.Value}";
        }

        if (artifact.Metadata.TryGetValue(MetadataKeys.Page, out var page) && !string.IsNullOrWhiteSpace(page))
        {
            return $"page {page.Trim()}";
        }

        if (artifact.Metadata.TryGetValue(MetadataKeys.Slide, out var slide) && !string.IsNullOrWhiteSpace(slide))
        {
            return $"slide {slide.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(artifact.Source))
        {
            return artifact.Source!.Trim();
        }

        return null;
    }

    private static IEnumerable<string> ExtractVisibleText(ImageArtifact artifact)
    {
        if (!string.IsNullOrWhiteSpace(artifact.RawText))
        {
            foreach (var line in SplitLines(artifact.RawText))
            {
                yield return line;
            }
        }

        if (artifact.Metadata.TryGetValue(MetadataKeys.OcrText, out var ocr) && !string.IsNullOrWhiteSpace(ocr))
        {
            foreach (var line in SplitLines(ocr))
            {
                yield return line;
            }
        }
    }

    private static IEnumerable<string> SplitLines(string value)
    {
        return value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0);
    }

    private static bool AppendListSection(StringBuilder builder, string heading, IEnumerable<string> items)
    {
        var materialized = items
            .Select(static item => item.ReplaceLineEndings(" ").Trim())
            .Where(static item => item.Length > 0)
            .ToList();

        if (materialized.Count == 0)
        {
            return false;
        }

        builder.AppendLine($"{heading}:");
        foreach (var item in materialized)
        {
            builder.AppendLine($"- {item}");
        }

        builder.AppendLine();
        return true;
    }

    private static bool AppendLayoutSection(StringBuilder builder, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var items = new List<string>();

            foreach (var element in document.RootElement.EnumerateArray())
            {
                var parts = new List<string>();
                if (element.TryGetProperty("position", out var position) && position.ValueKind == JsonValueKind.String)
                {
                    parts.Add($"position: {position.GetString()}");
                }

                if (element.TryGetProperty("highlightColor", out var color) && color.ValueKind == JsonValueKind.String)
                {
                    parts.Add($"highlight: {color.GetString()}");
                }

                if (element.TryGetProperty("decoration", out var decoration) && decoration.ValueKind == JsonValueKind.String)
                {
                    parts.Add($"decoration: {decoration.GetString()}");
                }

                if (element.TryGetProperty("elements", out var elements) && elements.ValueKind == JsonValueKind.Array)
                {
                    var listed = elements
                        .EnumerateArray()
                        .Where(static e => e.ValueKind == JsonValueKind.String)
                        .Select(static e => e.GetString())
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .Select(static value => value!.Trim())
                        .ToList();

                    if (listed.Count > 0)
                    {
                        parts.Add("elements: " + string.Join(", ", listed));
                    }
                }

                if (element.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(notes.GetString()))
                {
                    parts.Add("notes: " + notes.GetString());
                }

                var id = element.TryGetProperty("id", out var identifier) && identifier.ValueKind == JsonValueKind.String
                    ? identifier.GetString()
                    : null;

                var summary = id switch
                {
                    null when parts.Count == 0 => null,
                    null => string.Join("; ", parts),
                    _ when parts.Count == 0 => id,
                    _ => $"{id} – {string.Join("; ", parts)}"
                };

                if (!string.IsNullOrWhiteSpace(summary))
                {
                    items.Add(summary!);
                }
            }

            return AppendListSection(builder, "Layout", items);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool AppendUiSection(StringBuilder builder, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var items = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var label = element.TryGetProperty("label", out var labelProp) && labelProp.ValueKind == JsonValueKind.String
                    ? labelProp.GetString()
                    : "Element";

                var parts = new List<string>();
                if (element.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                {
                    parts.Add(type.GetString()!);
                }

                if (element.TryGetProperty("regionId", out var region) && region.ValueKind == JsonValueKind.String)
                {
                    parts.Add($"region: {region.GetString()}");
                }

                if (element.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.String)
                {
                    parts.Add($"action: {action.GetString()}");
                }

                var summary = label;
                if (parts.Count > 0)
                {
                    summary += " – " + string.Join("; ", parts);
                }

                summary = string.IsNullOrWhiteSpace(summary) ? "Element" : summary.Trim();
                items.Add(summary);
            }

            return AppendListSection(builder, "UI elements", items);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool AppendHighlightsSection(StringBuilder builder, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var items = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var description = element.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String
                    ? desc.GetString()
                    : "Highlight";

                var parts = new List<string>();
                if (element.TryGetProperty("regionId", out var region) && region.ValueKind == JsonValueKind.String)
                {
                    parts.Add($"region: {region.GetString()}");
                }

                if (element.TryGetProperty("color", out var color) && color.ValueKind == JsonValueKind.String)
                {
                    parts.Add($"color: {color.GetString()}");
                }

                if (element.TryGetProperty("shape", out var shape) && shape.ValueKind == JsonValueKind.String)
                {
                    parts.Add($"shape: {shape.GetString()}");
                }

                if (parts.Count > 0)
                {
                    description += " – " + string.Join("; ", parts);
                }

                var normalized = string.IsNullOrWhiteSpace(description) ? "Highlight" : description.Trim();
                items.Add(normalized);
            }

            return AppendListSection(builder, "Highlights", items);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool AppendDataVisualsSection(StringBuilder builder, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var visuals = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var label = element.TryGetProperty("label", out var labelProp) && labelProp.ValueKind == JsonValueKind.String
                    ? labelProp.GetString()
                    : "Visual";

                var type = element.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String
                    ? typeProp.GetString()
                    : null;

                var summary = type is null ? label : $"{label} ({type})";
                var details = new List<string>();

                if (element.TryGetProperty("axes", out var axes) && axes.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(axes.GetString()))
                {
                    details.Add("Axes: " + axes.GetString());
                }

                if (element.TryGetProperty("metrics", out var metrics) && metrics.ValueKind == JsonValueKind.Array)
                {
                    var metricList = metrics
                        .EnumerateArray()
                        .Where(static m => m.ValueKind == JsonValueKind.String)
                        .Select(static m => m.GetString())
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .Select(static value => value!.Trim())
                        .ToList();

                    if (metricList.Count > 0)
                    {
                        details.Add("Metrics: " + string.Join(", ", metricList));
                    }
                }

                if (element.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(notes.GetString()))
                {
                    details.Add("Notes: " + notes.GetString());
                }

                if (details.Count > 0)
                {
                    summary += Environment.NewLine + "  - " + string.Join(Environment.NewLine + "  - ", details);
                }

                summary = string.IsNullOrWhiteSpace(summary) ? "Visual" : summary.Trim();
                visuals.Add(summary);
            }

            if (visuals.Count == 0)
            {
                return false;
            }

            builder.AppendLine("Data visuals:");
            foreach (var visual in visuals)
            {
                var lines = visual.Split(Environment.NewLine);
                builder.AppendLine("- " + lines[0]);
                for (var i = 1; i < lines.Length; i++)
                {
                    builder.AppendLine(lines[i]);
                }
            }

            builder.AppendLine();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool AppendTablesSection(StringBuilder builder, ImageArtifact artifact)
    {
        var tables = new List<string>();

        if (artifact.Metadata.TryGetValue(MetadataKeys.DataTableMarkdown, out var markdown) && !string.IsNullOrWhiteSpace(markdown))
        {
            tables.Add(markdown.Trim());
        }

        if (artifact.Metadata.TryGetValue(MetadataKeys.StructuredTables, out var structuredJson) && !string.IsNullOrWhiteSpace(structuredJson))
        {
            tables.AddRange(ParseStructuredTables(structuredJson));
        }

        if (tables.Count == 0)
        {
            return false;
        }

        builder.AppendLine("Table data:");
        foreach (var table in tables)
        {
            builder.AppendLine(table.Trim());
            builder.AppendLine();
        }

        return true;
    }

    private static IEnumerable<string> ParseStructuredTables(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var results = new List<string>();

            foreach (var element in document.RootElement.EnumerateArray())
            {
                var builder = new StringBuilder();

                if (element.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(title.GetString()))
                {
                    builder.AppendLine($"**{title.GetString()!.Trim()}**");
                    builder.AppendLine();
                }

                if (element.TryGetProperty("markdown", out var markdown) && markdown.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(markdown.GetString()))
                {
                    builder.AppendLine(markdown.GetString()!.Trim());
                }
                else
                {
                    var tableMarkdown = BuildMarkdownTable(element);
                    if (!string.IsNullOrWhiteSpace(tableMarkdown))
                    {
                        builder.AppendLine(tableMarkdown);
                    }
                }

                if (element.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(notes.GetString()))
                {
                    builder.AppendLine();
                    builder.AppendLine("Notes: " + notes.GetString());
                }

                results.Add(builder.ToString().TrimEnd());
            }

            return results;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string? BuildMarkdownTable(JsonElement element)
    {
        if (!element.TryGetProperty("headers", out var headersElement) || headersElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var headers = headersElement
            .EnumerateArray()
            .Where(static h => h.ValueKind == JsonValueKind.String)
            .Select(static h => h.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .ToList();

        if (headers.Count == 0)
        {
            return null;
        }

        var rows = new List<IReadOnlyList<string>>();
        if (element.TryGetProperty("rows", out var rowsElement) && rowsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rowsElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var values = row
                    .EnumerateArray()
                    .Select(static cell => cell.ValueKind == JsonValueKind.String ? cell.GetString() ?? string.Empty : cell.ToString())
                    .Select(static value => value.Trim())
                    .Cast<string>()
                    .ToList();

                rows.Add(values);
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine("| " + string.Join(" | ", headers.Select(static header => header.Replace("|", "\\|", StringComparison.Ordinal))) + " |");
        builder.AppendLine("| " + string.Join(" | ", headers.Select(static _ => "---")) + " |");

        foreach (var row in rows)
        {
            var cells = new List<string>();
            for (var i = 0; i < headers.Count; i++)
            {
                var value = i < row.Count ? row[i] : string.Empty;
                cells.Add(value.Replace("|", "\\|", StringComparison.Ordinal));
            }

            builder.AppendLine("| " + string.Join(" | ", cells) + " |");
        }

        return builder.ToString().TrimEnd();
    }

    private static bool AppendAsciiArt(StringBuilder builder, string? asciiArt)
    {
        if (string.IsNullOrWhiteSpace(asciiArt))
        {
            return false;
        }

        builder.AppendLine("ASCII sketch:");
        builder.AppendLine("```text");
        builder.AppendLine(asciiArt.TrimEnd());
        builder.AppendLine("```");
        builder.AppendLine();
        return true;
    }

    private static bool AppendMermaid(StringBuilder builder, ImageArtifact artifact)
    {
        var mermaid = artifact.MermaidDiagram;
        if (string.IsNullOrWhiteSpace(mermaid) && artifact.Metadata.TryGetValue(MetadataKeys.MermaidDiagram, out var stored) && !string.IsNullOrWhiteSpace(stored))
        {
            mermaid = stored;
        }

        if (string.IsNullOrWhiteSpace(mermaid))
        {
            return false;
        }

        builder.AppendLine("Diagram as Mermaid code:");
        builder.AppendLine("```mermaid");
        builder.AppendLine(mermaid.Trim());
        builder.AppendLine("```");
        builder.AppendLine();
        return true;
    }

    private static void TrimTrailingBlankLines(StringBuilder builder)
    {
        for (var i = builder.Length - 1; i >= 0; i--)
        {
            var ch = builder[i];
            if (ch == '\n' || ch == '\r' || ch == ' ')
            {
                continue;
            }

            if (i < builder.Length - 1 && builder[i + 1] == '\n')
            {
                builder.Remove(i + 1, builder.Length - (i + 1));
                builder.AppendLine();
            }
            return;
        }
    }
}
