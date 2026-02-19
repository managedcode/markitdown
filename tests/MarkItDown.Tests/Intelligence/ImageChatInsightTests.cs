using System;
using System.Collections.Generic;
using MarkItDown.Intelligence.Models;
using Shouldly;
using Xunit;

namespace MarkItDown.Tests.Intelligence;

public static class ImageChatInsightTests
{
    private static readonly string[] descriptionExpectedLines = new[]
        {
            "<!-- Image description:",
            "Dashboard overview",
            "",
            "Visible text:",
            "- Header: Dashboard",
            "- Body: Current status",
            "",
            "Layout:",
            "- region1 – position: top-left; notes: Hero section",
            "",
            "UI elements:",
            "- Primary button – button; action: navigate",
            "",
            "Highlights:",
            "- Critical alert – color: red",
            "",
            "Notes:",
            "- Shows daily metrics",
            "",
            "-->"
        };

    [Fact]
    public static void ToMarkdown_WithDescriptionAndDetails_ProducesImageDescriptionComment()
    {
        var insight = new ImageChatInsight
        {
            Description = "Dashboard overview"
        };

        insight.TextRegions.Add(new ImageChatInsight.TextRegion { Label = "Header", Text = "Dashboard" });
        insight.TextRegions.Add(new ImageChatInsight.TextRegion { Label = "Body", Text = "Current status" });

        insight.LayoutRegions.Add(new ImageChatInsight.LayoutRegion
        {
            Id = "region1",
            Position = "top-left",
            Notes = "Hero section"
        });

        insight.UiElements.Add(new ImageChatInsight.UiElement
        {
            Label = "Primary button",
            Type = "button",
            Action = "navigate"
        });

        insight.Highlights.Add(new ImageChatInsight.Highlight
        {
            Description = "Critical alert",
            Color = "red"
        });

        insight.Notes.Add("Shows daily metrics");

        var markdown = Normalize(insight.ToMarkdown());
        var expected = Normalize(string.Join(Environment.NewLine, descriptionExpectedLines));

        markdown.ShouldBe(expected);
    }
    private static readonly string[] tableExpectedLines = new[]
            {
                "<!-- Image description:",
                "Table data:",
                "Title: SLA Targets",
                "| Service | Target |",
                "| --- | --- |",
                "| API | 99.9% |",
                "| UI | 99.5% |",
                "Notes: Weekly averages",
                "",
                "-->"
            };
    private static readonly string[] chartExpectedLines = new[]
            {
                "<!-- Chart data:",
                "Type: Bar chart",
                "Title: Workload Distribution",
                "Axes: x: teams, y: tasks",
                "Metrics:",
                "- Completed",
                "- Pending",
                "Data:",
                "| Team | Completed | Pending |",
                "| --- | --- | --- |",
                "| Alpha | 5 | 2 |",
                "| Beta | 3 | 4 |",
                "Notes: Data from Q1",
                "-->"
            };

    [Fact]
    public static void ToMarkdown_WithChartAndTable_ProducesSeparateBlocks()
    {
        var insight = new ImageChatInsight();

        var chart = new ImageChatInsight.Chart
        {
            Title = "Workload Distribution",
            Type = "Bar chart",
            Axes = "x: teams, y: tasks",
            Notes = "Data from Q1"
        };

        chart.Metrics.Add("Completed");
        chart.Metrics.Add("Pending");

        chart.Data = new ImageChatInsight.ImageTable
        {
            Headers = { "Team", "Completed", "Pending" },
            Rows =
            {
                new List<string> { "Alpha", "5", "2" },
                new List<string> { "Beta", "3", "4" }
            }
        };

        insight.Charts.Add(chart);

        var table = new ImageChatInsight.ImageTable
        {
            Title = "SLA Targets",
            Headers = { "Service", "Target" },
            Rows =
            {
                new List<string> { "API", "99.9%" },
                new List<string> { "UI", "99.5%" }
            },
            Notes = "Weekly averages"
        };

        insight.Tables.Add(table);

        var markdown = Normalize(insight.ToMarkdown());
        var expected = Normalize(string.Join(Environment.NewLine + Environment.NewLine, new[]
        {
            string.Join(Environment.NewLine, tableExpectedLines),
            string.Join(Environment.NewLine, chartExpectedLines)
        }));

        markdown.ShouldBe(expected);
    }

    private static string Normalize(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal);
}
