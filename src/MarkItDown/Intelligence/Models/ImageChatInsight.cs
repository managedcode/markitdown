using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkItDown.Intelligence.Models;

/// <summary>
/// Represents the structured payload returned by the AI image understanding provider.
/// </summary>
internal sealed class ImageChatInsight
{
    public string? Description { get; set; }

    public IList<string> Notes { get; set; } = new List<string>();

    public IList<TextRegion> TextRegions { get; set; } = new List<TextRegion>();

    public IList<Diagram> Diagrams { get; set; } = new List<Diagram>();

    public IList<Chart> Charts { get; set; } = new List<Chart>();

    public IList<ImageTable> Tables { get; set; } = new List<ImageTable>();

    public IList<LayoutRegion> LayoutRegions { get; set; } = new List<LayoutRegion>();

    public IList<UiElement> UiElements { get; set; } = new List<UiElement>();

    public IList<Highlight> Highlights { get; set; } = new List<Highlight>();

    public bool HasContent()
    {
        return IsMeaningful(Description)
            || Notes.Any(IsMeaningful)
            || TextRegions.Any(static region => region.HasContent())
            || Diagrams.Any(static diagram => diagram.HasContent())
            || Charts.Any(static chart => chart.HasContent())
            || Tables.Any(static table => table.HasContent())
            || LayoutRegions.Any(static region => region.HasContent())
            || UiElements.Any(static element => element.HasContent())
            || Highlights.Any(static highlight => highlight.HasContent());
    }

    public string ToMarkdown()
    {
        var sections = new List<string>();

        var descriptionBlock = BuildDescriptionBlock();
        if (!string.IsNullOrWhiteSpace(descriptionBlock))
        {
            sections.Add(descriptionBlock);
        }

        foreach (var diagram in Diagrams.Where(static d => d.HasContent()))
        {
            var block = BuildDiagramBlock(diagram);
            if (!string.IsNullOrWhiteSpace(block))
            {
                sections.Add(block);
            }
        }

        foreach (var chart in Charts.Where(static c => c.HasContent()))
        {
            var block = BuildChartBlock(chart);
            if (!string.IsNullOrWhiteSpace(block))
            {
                sections.Add(block);
            }
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections).Trim();
    }

    private string? BuildDescriptionBlock()
    {
        var hasText = TextRegions.Any(static region => region.HasContent());
        var hasLayout = LayoutRegions.Any(static region => region.HasContent());
        var hasUi = UiElements.Any(static element => element.HasContent());
        var hasHighlights = Highlights.Any(static highlight => highlight.HasContent());
        var hasNotes = Notes.Any(IsMeaningful);
        var hasTables = Tables.Any(static table => table.HasContent());

        if (!IsMeaningful(Description) && !hasText && !hasLayout && !hasUi && !hasHighlights && !hasNotes && !hasTables)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine("<!-- Image description:");

        if (IsMeaningful(Description))
        {
            builder.AppendLine(Description!.Trim());
            builder.AppendLine();
        }

        AppendList(builder, "Visible text", TextRegions
            .Where(static region => region.HasContent())
            .Select(static region => $"{region.LabelOrFallback}: {region.Text!.Trim()}"));

        AppendList(builder, "Layout", LayoutRegions
            .Where(static region => region.HasContent())
            .Select(static region => region.ToSummary()));

        AppendList(builder, "UI elements", UiElements
            .Where(static element => element.HasContent())
            .Select(static element => element.ToSummary()));

        AppendList(builder, "Highlights", Highlights
            .Where(static highlight => highlight.HasContent())
            .Select(static highlight => highlight.ToSummary()));

        if (Notes.Any(IsMeaningful))
        {
            builder.AppendLine("Notes:");
            foreach (var note in Notes.Where(IsMeaningful))
            {
                builder.Append("- ").AppendLine(note!.Trim());
            }

            builder.AppendLine();
        }

        var appendedTables = AppendTables(builder, Tables);
        if (appendedTables)
        {
            builder.AppendLine();
        }

        builder.Append("-->");
        return builder.ToString().TrimEnd();
    }

    private static bool AppendTables(StringBuilder builder, IEnumerable<ImageTable> tables)
    {
        var any = false;
        foreach (var table in tables.Where(static t => t.HasContent()))
        {
            var markdown = table.ToMarkdown();
            if (string.IsNullOrWhiteSpace(markdown) && !table.HasTabularData())
            {
                continue;
            }

            builder.AppendLine("Table data:");
            if (IsMeaningful(table.Title))
            {
                builder.Append("Title: ").AppendLine(table.Title!.Trim());
            }

            if (!string.IsNullOrWhiteSpace(markdown))
            {
                builder.AppendLine(markdown.Trim());
            }

            if (IsMeaningful(table.Notes))
            {
                builder.Append("Notes: ").AppendLine(table.Notes!.Trim());
            }

            builder.AppendLine();
            any = true;
        }

        if (any)
        {
            // remove the trailing blank line we just appended and leave a single newline for caller
            builder.Length -= Environment.NewLine.Length;
        }

        return any;
    }

    private static string? BuildDiagramBlock(Diagram diagram)
    {
        if (!diagram.HasContent())
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine("<!-- Diagram as Mermaid code:");
        if (IsMeaningful(diagram.Title))
        {
            builder.Append("Title: ").AppendLine(diagram.Title!.Trim());
        }

        var mermaid = diagram.Mermaid?.Trim();
        if (!string.IsNullOrEmpty(mermaid))
        {
            builder.AppendLine("```mermaid");
            builder.AppendLine(mermaid);
            builder.AppendLine("```");
        }

        if (IsMeaningful(diagram.Notes))
        {
            builder.Append("Notes: ").AppendLine(diagram.Notes!.Trim());
        }

        builder.Append("-->");
        return builder.ToString().TrimEnd();
    }

    private static string? BuildChartBlock(Chart chart)
    {
        if (!chart.HasContent())
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine("<!-- Chart data:");
        if (IsMeaningful(chart.Type))
        {
            builder.Append("Type: ").AppendLine(chart.Type!.Trim());
        }

        if (IsMeaningful(chart.Title))
        {
            builder.Append("Title: ").AppendLine(chart.Title!.Trim());
        }

        if (IsMeaningful(chart.XAxis))
        {
            builder.Append("X-axis: ").AppendLine(chart.XAxis!.Trim());
        }

        if (IsMeaningful(chart.YAxis))
        {
            builder.Append("Y-axis: ").AppendLine(chart.YAxis!.Trim());
        }

        if (!IsMeaningful(chart.XAxis) && !IsMeaningful(chart.YAxis) && IsMeaningful(chart.Axes))
        {
            builder.Append("Axes: ").AppendLine(chart.Axes!.Trim());
        }

        if (chart.Metrics.Any(IsMeaningful))
        {
            builder.AppendLine("Metrics:");
            foreach (var metric in chart.Metrics.Where(IsMeaningful))
            {
                builder.Append("- ").AppendLine(metric!.Trim());
            }
        }

        if (chart.Data?.HasContent() == true)
        {
            builder.AppendLine("Data:");
            builder.AppendLine(chart.Data.ToMarkdown());
        }

        if (IsMeaningful(chart.Trend))
        {
            builder.Append("Trend: ").AppendLine(chart.Trend!.Trim());
        }

        if (IsMeaningful(chart.Notes))
        {
            builder.Append("Notes: ").AppendLine(chart.Notes!.Trim());
        }

        builder.Append("-->");
        return builder.ToString().TrimEnd();
    }

    private static void AppendList(StringBuilder builder, string heading, IEnumerable<string> items)
    {
        var materialized = items
            .Where(static item => IsMeaningful(item))
            .Select(static item => item!.Trim())
            .ToList();

        if (materialized.Count == 0)
        {
            return;
        }

        builder.AppendLine($"{heading}:");
        foreach (var item in materialized)
        {
            builder.Append("- ").AppendLine(item);
        }

        builder.AppendLine();
    }

    internal static bool IsMeaningful(string? value)
        => !string.IsNullOrWhiteSpace(value) && !string.Equals(value.Trim(), "null", StringComparison.OrdinalIgnoreCase);

    internal sealed class TextRegion
    {
        public string? Label { get; set; }

        public string? Text { get; set; }

        public bool HasContent() => IsMeaningful(Text);

        public string LabelOrFallback => IsMeaningful(Label) ? Label!.Trim() : "Text";
    }

    internal sealed class Diagram
    {
        public string? Title { get; set; }

        public string? Mermaid { get; set; }

        [JsonConverter(typeof(SingleOrArrayToStringConverter))]
        public string? Notes { get; set; }

        public bool HasContent()
            => IsMeaningful(Mermaid) || IsMeaningful(Title) || IsMeaningful(Notes);
    }

    internal sealed class Chart
    {
        public string? Title { get; set; }

        public string? Type { get; set; }

        public string? Axes { get; set; }

        public string? XAxis { get; set; }

        public string? YAxis { get; set; }

        public IList<string> Metrics { get; set; } = new List<string>();

        public ImageTable? Data { get; set; }

        [JsonConverter(typeof(SingleOrArrayToStringConverter))]
        public string? Notes { get; set; }

        public string? Trend { get; set; }

        public bool HasContent()
        {
            return IsMeaningful(Title) ||
                   IsMeaningful(Type) ||
                   IsMeaningful(Axes) ||
                   IsMeaningful(XAxis) ||
                   IsMeaningful(YAxis) ||
                   Metrics.Any(IsMeaningful) ||
                   (Data?.HasContent() == true) ||
                   IsMeaningful(Notes) ||
                   IsMeaningful(Trend);
        }
    }

    internal sealed class ImageTable
    {
        public string? Title { get; set; }

        public IList<string> Headers { get; set; } = new List<string>();

        public IList<IList<string>> Rows { get; set; } = new List<IList<string>>();

        public string? Markdown { get; set; }

        [JsonConverter(typeof(SingleOrArrayToStringConverter))]
        public string? Notes { get; set; }

        public bool HasContent()
        {
            return HasTabularData() || IsMeaningful(Markdown) || IsMeaningful(Title) || IsMeaningful(Notes);
        }

        public bool HasTabularData()
        {
            return Headers.Any(IsMeaningful) || Rows.Any(static row => row?.Any(IsMeaningful) == true);
        }

        public string ToMarkdown()
        {
            if (IsMeaningful(Markdown))
            {
                return Markdown!.Trim();
            }

            var headers = Headers.Where(IsMeaningful).Select(static header => header!.Trim()).ToList();
            if (headers.Count == 0 || Rows.Count == 0)
            {
                return string.Empty;
            }

            var normalizedRows = Rows
                .Select(row => row?.Select(cell => cell ?? string.Empty).ToList() ?? new List<string>())
                .ToList();

            foreach (var row in normalizedRows)
            {
                while (row.Count < headers.Count)
                {
                    row.Add(string.Empty);
                }

                if (row.Count > headers.Count)
                {
                    row.RemoveRange(headers.Count, row.Count - headers.Count);
                }
            }

            PropagateEmptyCells(normalizedRows, startRow: 0);

            var builder = new StringBuilder();
            builder.Append('|');
            foreach (var header in headers)
            {
                builder.Append(' ').Append(header.Replace("|", "\\|", StringComparison.Ordinal)).Append(" |");
            }

            builder.AppendLine();
            builder.Append('|');
            foreach (var _ in headers)
            {
                builder.Append(" --- |");
            }

            builder.AppendLine();

            foreach (var row in normalizedRows)
            {
                builder.Append('|');
                foreach (var cell in row)
                {
                    var content = IsMeaningful(cell)
                        ? cell!.Trim().Replace("|", "\\|", StringComparison.Ordinal)
                        : string.Empty;
                    builder.Append(' ').Append(content).Append(" |");
                }

                builder.AppendLine();
            }

            return builder.ToString().TrimEnd();
        }

        private static void PropagateEmptyCells(List<List<string>> rows, int startRow)
        {
            if (rows.Count <= startRow)
            {
                return;
            }

            var columnCount = rows[startRow].Count;
            for (var col = 0; col < columnCount; col++)
            {
                string? lastValue = null;
                for (var rowIndex = startRow; rowIndex < rows.Count; rowIndex++)
                {
                    var value = rows[rowIndex][col];
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        if (!string.IsNullOrWhiteSpace(lastValue))
                        {
                            rows[rowIndex][col] = lastValue!;
                        }
                    }
                    else
                    {
                        lastValue = value;
                    }
                }
            }
        }
    }

    internal sealed class LayoutRegion
    {
        public string? Id { get; set; }

        public string? Position { get; set; }

        public string? HighlightColor { get; set; }

        public string? Decoration { get; set; }

        public IList<string> Elements { get; set; } = new List<string>();

        [JsonConverter(typeof(SingleOrArrayToStringConverter))]
        public string? Notes { get; set; }

        public bool HasContent()
        {
            return IsMeaningful(Id) ||
                   IsMeaningful(Position) ||
                   IsMeaningful(HighlightColor) ||
                   IsMeaningful(Decoration) ||
                   IsMeaningful(Notes) ||
                   Elements.Any(IsMeaningful);
        }

        public string ToSummary()
        {
            var name = IsMeaningful(Id) ? Id!.Trim() : "Region";
            var parts = new List<string>();

            if (IsMeaningful(Position))
            {
                parts.Add("position: " + Position!.Trim());
            }

            if (IsMeaningful(HighlightColor))
            {
                parts.Add("highlight: " + HighlightColor!.Trim());
            }

            if (IsMeaningful(Decoration))
            {
                parts.Add("decoration: " + Decoration!.Trim());
            }

            var elements = Elements.Where(IsMeaningful).Select(static element => element!.Trim()).ToList();
            if (elements.Count > 0)
            {
                parts.Add("elements: " + string.Join(", ", elements));
            }

            if (IsMeaningful(Notes))
            {
                parts.Add("notes: " + Notes!.Trim());
            }

            return parts.Count == 0 ? name : $"{name} – {string.Join("; ", parts)}";
        }
    }

    internal sealed class UiElement
    {
        public string? Label { get; set; }

        public string? Type { get; set; }

        public string? RegionId { get; set; }

        public string? Action { get; set; }

        public bool HasContent()
        {
            return IsMeaningful(Label) || IsMeaningful(Type) || IsMeaningful(RegionId) || IsMeaningful(Action);
        }

        public string ToSummary()
        {
            var label = IsMeaningful(Label) ? Label!.Trim() : "Element";
            var details = new List<string>();

            if (IsMeaningful(Type))
            {
                details.Add(Type!.Trim());
            }

            if (IsMeaningful(RegionId))
            {
                details.Add("region: " + RegionId!.Trim());
            }

            if (IsMeaningful(Action))
            {
                details.Add("action: " + Action!.Trim());
            }

            return details.Count == 0 ? label : $"{label} – {string.Join("; ", details)}";
        }
    }

    internal sealed class Highlight
    {
        public string? RegionId { get; set; }

        public string? Color { get; set; }

        public string? Shape { get; set; }

        public string? Description { get; set; }

        public bool HasContent()
        {
            return IsMeaningful(RegionId) || IsMeaningful(Color) || IsMeaningful(Shape) || IsMeaningful(Description);
        }

        public string ToSummary()
        {
            var description = IsMeaningful(Description) ? Description!.Trim() : "Highlight";
            var details = new List<string>();

            if (IsMeaningful(RegionId))
            {
                details.Add("region: " + RegionId!.Trim());
            }

            if (IsMeaningful(Color))
            {
                details.Add("color: " + Color!.Trim());
            }

            if (IsMeaningful(Shape))
            {
                details.Add("shape: " + Shape!.Trim());
            }

            return details.Count == 0 ? description : $"{description} – {string.Join("; ", details)}";
        }
    }
    private sealed class SingleOrArrayToStringConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => Normalize(reader.GetString()),
                JsonTokenType.StartArray => ReadArray(ref reader),
                JsonTokenType.Null => null,
                _ => SkipAndReturnNull(ref reader)
            };
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(value);
        }

        private static string? ReadArray(ref Utf8JsonReader reader)
        {
            var parts = new List<string>();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.String)
                {
                    var item = Normalize(reader.GetString());
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        parts.Add(item!);
                    }
                }
                else
                {
                    reader.Skip();
                }
            }

            return parts.Count == 0 ? null : string.Join("; ", parts);
        }

        private static string? SkipAndReturnNull(ref Utf8JsonReader reader)
        {
            reader.Skip();
            return null;
        }

        private static string? Normalize(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
