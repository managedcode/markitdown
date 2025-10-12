using System;
using System.IO;
using System.Threading.Tasks;
using MarkItDown;
using Shouldly;
using Xunit;

namespace MarkItDown.Tests;

public sealed class NewFormatsConverterTests
{
    private const string SampleOdtBase64 = "UEsDBBQAAAAIAABbTFuvsRNW0gAAAJgBAAALAAAAY29udGVudC54bWyNkDFuwzAMRXefgtDupN0KwVaWImuHOgdQZToWIJOCJAfJ7SvZTtpm6kTw6z/qk83hOjm4YIiWqRWvuxcBSIZ7S+dWnLpj/SYOqmp4GKxB2bOZJ6RUG6aUK2SaolxfWzEHkqyjjZL0hFEmI9kj3Sn52y3LX9U2IOE1/Rcv3gVWFcA92Bf3t9L/KMW2KllbmBGWwnNylrB2eEGXVxbqU0/eIXy8d81+df4FvepGG8HroM9B+xEM53gwBJ5AU+EgLiN2G++3KPunLA9hjfton8+qqm9QSwECFAMUAAAACAAAW0xbr7ETVtIAAACYAQAACwAAAAAAAAAAAAAAgAEAAAAAY29udGVudC54bWxQSwUGAAAAAAEAAQA5AAAA+wAAAAAA";
    [Theory]
    [InlineData(TestAssetCatalog.StellarObservationDocbook)]
    [InlineData(TestAssetCatalog.ObservationHandbookDbk)]
    public async Task DocBookConverter_ProducesSectionHeadings(string fileName)
    {
        var result = await ConvertAsync(fileName);
        if (fileName == TestAssetCatalog.ObservationHandbookDbk)
        {
            result.Markdown.ShouldContain("## Field Operations");
        }
        else
        {
            result.Markdown.ShouldContain("## Stellar Observation Compendium");
        }
    }

    [Theory]
    [InlineData(TestAssetCatalog.StellarObservationJats)]
    [InlineData(TestAssetCatalog.AstrodynamicsStudyBits)]
    public async Task JatsConverter_UsesArticleTitle(string fileName)
    {
        var result = await ConvertAsync(fileName);
        if (fileName == TestAssetCatalog.StellarObservationJats)
        {
            result.Markdown.ShouldContain("Helios Stellar Observation Record");
            result.Markdown.ShouldContain("Observation Summary");
        }
        else
        {
            result.Markdown.ShouldContain("Astrodynamics Study Notes");
            result.Markdown.ShouldContain("Delta-V Ledger");
        }
    }

    [Fact]
    public async Task OpmlConverter_RendersOutline()
    {
        var result = await ConvertAsync(TestAssetCatalog.MissionOutlineOpml);
        result.Markdown.ShouldContain("- Mission Overview");
        result.Markdown.ShouldContain("- Science Operations");
    }

    [Fact]
    public async Task Fb2Converter_WritesSectionHeading()
    {
        var result = await ConvertAsync(TestAssetCatalog.ExplorerJournalFb2);
        result.Markdown.ShouldContain("# Sol Day 142 — Debris Field");
        result.Markdown.ShouldContain("## Science Log");
    }

    [Fact]
    public async Task OdtConverter_LoadsContentXml()
    {
        var markItDown = new MarkItDownClient();
        var bytes = Convert.FromBase64String(SampleOdtBase64);
        var tempPath = Path.Combine(Path.GetTempPath(), $"markitdown-{Guid.NewGuid():N}.odt");
        await File.WriteAllBytesAsync(tempPath, bytes);

        try
        {
            var result = await markItDown.ConvertAsync(tempPath);
            result.Markdown.ShouldContain("Sample ODT");
            result.Markdown.ShouldContain("paragraph comes from an ODT");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task RtfConverter_ExtractsPlainText()
    {
        var result = await ConvertAsync(TestAssetCatalog.CrewBriefingRtf);
        result.Markdown.ShouldContain("Helios Crew Briefing");
        result.Markdown.ShouldContain("Navigation summary");
    }

    [Theory]
    [InlineData(TestAssetCatalog.NavigationTheoryTex)]
    [InlineData(TestAssetCatalog.NavigationTheoryLatex)]
    public async Task LatexConverter_ConvertsSections(string fileName)
    {
        var result = await ConvertAsync(fileName);
        result.Markdown.ShouldContain("telemetry-events.jsonl");
        if (fileName == TestAssetCatalog.NavigationTheoryTex)
        {
            result.Markdown.ShouldContain("# Burn Planning Model");
        }
        else
        {
            result.Markdown.ShouldContain("# Reference Frames");
        }
    }

    [Theory]
    [InlineData(TestAssetCatalog.EngineeringNotesRst)]
    [InlineData(TestAssetCatalog.EngineeringNotesRest)]
    public async Task RstConverter_ConvertsHeadings(string fileName)
    {
        var result = await ConvertAsync(fileName);
        result.Markdown.ShouldContain("# Helios Engineering Notes");
        result.Markdown.ShouldContain("telemetry-events.jsonl");
    }

    [Fact]
    public async Task AsciiDocConverter_ConvertsHeadings()
    {
        var result = await ConvertAsync(TestAssetCatalog.CelestialNavigationNotesAdoc);
        result.Markdown.ShouldContain("# Helios Navigation Operations Log");
        result.Markdown.ShouldContain("Mission Operations Guide");
    }

    [Fact]
    public async Task OrgConverter_ConvertsHeadings()
    {
        var result = await ConvertAsync(TestAssetCatalog.MissionChecklistOrg);
        result.Markdown.ShouldContain("Helios Mission Daily Checklist");
        result.Markdown.ShouldContain("celestial-navigation-notes.adoc");
    }

    [Theory]
    [InlineData(TestAssetCatalog.ObservatoryLogDj)]
    [InlineData(TestAssetCatalog.ObservatoryLogDjot)]
    public async Task DjotConverter_PassesThroughContent(string fileName)
    {
        var result = await ConvertAsync(fileName);
        result.Markdown.ShouldContain("Helios Observatory Shift Log");
        result.Markdown.ShouldContain("mission-outline.opml");
    }

    [Theory]
    [InlineData(TestAssetCatalog.NavigationOverviewTyp)]
    [InlineData(TestAssetCatalog.NavigationOverviewTypst)]
    public async Task TypstConverter_ConvertsHeading(string fileName)
    {
        var result = await ConvertAsync(fileName);
        result.Markdown.ShouldContain("Navigation Overview");
        result.Markdown.ShouldContain("mission-summary.metamd");
    }

    [Fact]
    public async Task TextileConverter_ConvertsHeading()
    {
        var result = await ConvertAsync(TestAssetCatalog.CrewHandbookTextile);
        result.Markdown.ShouldContain("# Helios Crew Handbook (Excerpt)");
        result.Markdown.ShouldContain("mission-network");
    }

    [Theory]
    [InlineData(TestAssetCatalog.MissionWikiWiki, "# Helios Mission Knowledge Base")]
    [InlineData(TestAssetCatalog.LunarResearchMediawiki, "# Helios Lunar Research Hub")]
    [InlineData(TestAssetCatalog.MissionOperationsCreole, "# Helios Mission Operations Guide")]
    [InlineData(TestAssetCatalog.MissionBriefingDokuwiki, "###### Helios Daily Briefing")]
    public async Task WikiMarkupConverter_ConvertsLink(string fileName, string expectedHeading)
    {
        var result = await ConvertAsync(fileName);
        result.Markdown.ShouldContain(expectedHeading);
        result.Markdown.ShouldContain("mission");
    }

    [Theory]
    [InlineData(TestAssetCatalog.OrbitalResearchBib)]
    [InlineData(TestAssetCatalog.OrbitalResearchExtendedBibtex)]
    public async Task BibTexConverter_RendersBibliography(string fileName)
    {
        var result = await ConvertAsync(fileName);
        if (fileName == TestAssetCatalog.OrbitalResearchBib)
        {
            result.Markdown.ShouldContain("Adaptive Course Control");
        }
        else
        {
            result.Markdown.ShouldContain("SOLID Navigation Principles");
        }
        result.Markdown.ShouldContain("Helios");
    }

    [Fact]
    public async Task RisConverter_RendersEntries()
    {
        var result = await ConvertAsync(TestAssetCatalog.OrbitalResearchRis);
        result.Markdown.ShouldContain("Adaptive Course Control");
        result.Markdown.ShouldContain("Trustworthy Telemetry");
    }

    [Theory]
    [InlineData(TestAssetCatalog.OrbitalResearchEndnoteXml, "Helios Knowledge Office")]
    [InlineData(TestAssetCatalog.OrbitalResearchEnl, null)]
    [InlineData(TestAssetCatalog.OrbitalResearchEndnote, null)]
    public async Task EndNoteXmlConverter_RendersBibliography(string fileName, string? expectedAuthor)
    {
        var result = await ConvertAsync(fileName);
        result.Markdown.ShouldContain("Helios");
        switch (fileName)
        {
            case TestAssetCatalog.OrbitalResearchEndnoteXml:
                result.Markdown.ShouldContain("Helios Systems Guide Collection");
                break;
            case TestAssetCatalog.OrbitalResearchEnl:
                result.Markdown.ShouldContain("Autonomous Observatory Field Manual");
                break;
            case TestAssetCatalog.OrbitalResearchEndnote:
                result.Markdown.ShouldContain("Adaptive Course Control");
                break;
        }
        if (!string.IsNullOrWhiteSpace(expectedAuthor))
        {
            result.Markdown.ShouldContain(expectedAuthor);
        }
    }

    [Fact]
    public async Task CslJsonConverter_RendersReference()
    {
        var result = await ConvertAsync(TestAssetCatalog.MissionCitationsCsljson);
        result.Markdown.ShouldContain("Adaptive Course Control");
        result.Markdown.ShouldContain("Trustworthy Telemetry Pipelines");
    }

    [Theory]
    [InlineData(TestAssetCatalog.TelemetryEventsJsonl, "\"event_id\": \"JOV-3112\"")]
    [InlineData(TestAssetCatalog.TelemetryEventsNdjson, "\"stage\":\"analysis\"")]
    public async Task JsonConverter_SupportsJsonLines(string fileName, string expectedFragment)
    {
        var result = await ConvertAsync(fileName);
        result.Markdown.ShouldContain("```json");
        result.Markdown.ShouldContain(expectedFragment);
    }

    [Theory]
    [InlineData(TestAssetCatalog.ResourceAllocationTsv)]
    [InlineData(TestAssetCatalog.ResourceAllocationTab)]
    public async Task CsvConverter_SupportsDelimitedFiles(string fileName)
    {
        var markItDown = new MarkItDownClient();
        var path = TestAssetLoader.GetAssetPath(fileName);
        var result = await markItDown.ConvertAsync(path);
        result.Markdown.ShouldContain("| Subsystem | Resource | Units | Reference |");
        result.Markdown.ShouldContain("| Navigation | Reaction Mass (kg) | 42 |");
    }

    [Theory]
    [InlineData(TestAssetCatalog.MissionFlowchartMermaid)]
    [InlineData(TestAssetCatalog.MissionFlowchartMmd)]
    public async Task MermaidConverter_WrapsFencedBlock(string fileName)
    {
        var result = await ConvertAsync(fileName);
        result.Markdown.ShouldStartWith("```mermaid");
        if (fileName == TestAssetCatalog.MissionFlowchartMermaid)
        {
            result.Markdown.ShouldContain("celestial-navigation-notes.adoc");
        }
        else
        {
            result.Markdown.ShouldContain("telemetry-events.ndjson");
        }
    }

    [Theory]
    [InlineData(TestAssetCatalog.MissionNetworkDot)]
    [InlineData(TestAssetCatalog.MissionNetworkGv)]
    public async Task GraphvizConverter_WrapsFencedBlock(string fileName)
    {
        var result = await ConvertAsync(fileName);
        result.Markdown.ShouldStartWith("```dot");
        result.Markdown.ShouldContain("Helios Vehicle");
    }

    [Theory]
    [InlineData(TestAssetCatalog.DeploymentDiagramPuml)]
    [InlineData(TestAssetCatalog.DeploymentDiagramPlantuml)]
    [InlineData(TestAssetCatalog.DeploymentDiagramWsd)]
    public async Task PlantUmlConverter_WrapsFencedBlock(string fileName)
    {
        var result = await ConvertAsync(fileName);
        result.Markdown.ShouldStartWith("```plantuml");
        switch (fileName)
        {
            case TestAssetCatalog.DeploymentDiagramWsd:
                result.Markdown.ShouldContain("Crew->Ops");
                break;
            case TestAssetCatalog.DeploymentDiagramPuml:
                result.Markdown.ShouldContain("Crew Console");
                break;
            default:
                result.Markdown.ShouldContain("Crew Terminal");
                break;
        }
    }

    [Fact]
    public async Task TikzConverter_WrapsLatexBlock()
    {
        var result = await ConvertAsync(TestAssetCatalog.DeploymentDiagramTikz);
        result.Markdown.ShouldStartWith("```latex");
        result.Markdown.ShouldContain("Crew Terminal");
    }

    [Fact]
    public async Task MetaMdConverter_ExpandsMetadataAndReferences()
    {
        var result = await ConvertAsync(TestAssetCatalog.MissionSummaryMetamd);
        result.Markdown.ShouldContain("\"title\": \"Helios Mission Cross-System Summary\"");
        result.Markdown.ShouldContain("Adaptive Course Control");
        result.Markdown.ShouldContain("```mermaid");
    }

    private static async Task<DocumentConverterResult> ConvertAsync(string fileName)
    {
        var markItDown = new MarkItDownClient();
        var path = TestAssetLoader.GetAssetPath(fileName);
        return await markItDown.ConvertAsync(path);
    }
}
