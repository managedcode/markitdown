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
    [Fact]
    public async Task DocBookConverter_ProducesSectionHeadings()
    {
        var result = await ConvertAsync("sample.docbook");
        result.Markdown.ShouldContain("## Details");
    }

    [Fact]
    public async Task JatsConverter_UsesArticleTitle()
    {
        var result = await ConvertAsync("sample.jats");
        result.Markdown.ShouldContain("Sample JATS Article");
        result.Markdown.ShouldContain("## Background");
    }

    [Fact]
    public async Task OpmlConverter_RendersOutline()
    {
        var result = await ConvertAsync("sample.opml");
        result.Markdown.ShouldContain("- Parent");
        result.Markdown.ShouldContain("Child 1");
    }

    [Fact]
    public async Task Fb2Converter_WritesSectionHeading()
    {
        var result = await ConvertAsync("sample.fb2");
        result.Markdown.ShouldContain("## Section Heading");
        result.Markdown.ShouldContain("FictionBook paragraphs");
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
        var result = await ConvertAsync("sample.rtf");
        result.Markdown.ShouldContain("Sample RTF Document");
        result.Markdown.ShouldContain("italics");
    }

    [Fact]
    public async Task LatexConverter_ConvertsSections()
    {
        var result = await ConvertAsync("sample.tex");
        result.Markdown.ShouldContain("# Overview");
        result.Markdown.ShouldContain("- First item");
    }

    [Fact]
    public async Task RstConverter_ConvertsHeadings()
    {
        var result = await ConvertAsync("sample.rst");
        result.Markdown.ShouldContain("# Sample RST Heading");
        result.Markdown.ShouldContain("`inline code`");
    }

    [Fact]
    public async Task AsciiDocConverter_ConvertsHeadings()
    {
        var result = await ConvertAsync("sample.adoc");
        result.Markdown.ShouldContain("# Sample AsciiDoc");
        result.Markdown.ShouldContain("**bold**");
    }

    [Fact]
    public async Task OrgConverter_ConvertsHeadings()
    {
        var result = await ConvertAsync("sample.org");
        result.Markdown.ShouldContain("# Sample Org");
        result.Markdown.ShouldContain("- First bullet");
    }

    [Fact]
    public async Task DjotConverter_PassesThroughContent()
    {
        var result = await ConvertAsync("sample.dj");
        result.Markdown.ShouldContain("Sample Djot");
    }

    [Fact]
    public async Task TypstConverter_ConvertsHeading()
    {
        var result = await ConvertAsync("sample.typ");
        result.Markdown.ShouldContain("# Sample Typst");
        result.Markdown.ShouldContain("- item one");
    }

    [Fact]
    public async Task TextileConverter_ConvertsHeading()
    {
        var result = await ConvertAsync("sample.textile");
        result.Markdown.ShouldContain("# Sample Textile");
        result.Markdown.ShouldContain("1. Numbered one");
    }

    [Fact]
    public async Task WikiMarkupConverter_ConvertsLink()
    {
        var result = await ConvertAsync("sample.wiki");
        result.Markdown.ShouldContain("# Sample Wiki");
        result.Markdown.ShouldContain("[Example](https://example.com)");
    }

    [Fact]
    public async Task BibTexConverter_RendersBibliography()
    {
        var result = await ConvertAsync("sample.bib");
        result.Markdown.ShouldContain("Sample Entry");
        result.Markdown.ShouldContain("Ada Lovelace");
    }

    [Fact]
    public async Task RisConverter_RendersEntries()
    {
        var result = await ConvertAsync("sample.ris");
        result.Markdown.ShouldContain("Sample RIS Entry");
        result.Markdown.ShouldContain("https://example.com/ris");
    }

    [Fact]
    public async Task EndNoteXmlConverter_RendersAuthors()
    {
        var result = await ConvertAsync("sample.endnote.xml");
        result.Markdown.ShouldContain("Sample EndNote");
        result.Markdown.ShouldContain("Test Researcher");
    }

    [Fact]
    public async Task CslJsonConverter_RendersReference()
    {
        var result = await ConvertAsync("sample.csljson");
        result.Markdown.ShouldContain("CSL JSON Entry");
        result.Markdown.ShouldContain("https://example.com/csl");
    }

    [Fact]
    public async Task CsvConverter_SupportsTsv()
    {
        var markItDown = new MarkItDownClient();
        var path = TestAssetLoader.GetAssetPath("sample.tsv");
        var result = await markItDown.ConvertAsync(path);
        result.Markdown.ShouldContain("| Name | Value |");
        result.Markdown.ShouldContain("| Alpha | 1 |");
    }

    [Fact]
    public async Task MermaidConverter_WrapsFencedBlock()
    {
        var result = await ConvertAsync("sample.mermaid");
        result.Markdown.ShouldStartWith("```mermaid");
        result.Markdown.ShouldContain("A[Start]");
    }

    [Fact]
    public async Task GraphvizConverter_WrapsFencedBlock()
    {
        var result = await ConvertAsync("sample.dot");
        result.Markdown.ShouldStartWith("```dot");
        result.Markdown.ShouldContain("A -> B");
    }

    [Fact]
    public async Task PlantUmlConverter_WrapsFencedBlock()
    {
        var result = await ConvertAsync("sample.puml");
        result.Markdown.ShouldStartWith("```plantuml");
        result.Markdown.ShouldContain("Alice");
    }

    [Fact]
    public async Task TikzConverter_WrapsLatexBlock()
    {
        var result = await ConvertAsync("sample.tikz");
        result.Markdown.ShouldStartWith("```latex");
        result.Markdown.ShouldContain("\\draw");
    }

    [Fact]
    public async Task MetaMdConverter_ExpandsMetadataAndReferences()
    {
        var result = await ConvertAsync("sample.metamd");
        result.Markdown.ShouldContain("# MetaMD Sample");
        result.Markdown.ShouldContain("Sketch of the Analytical Engine");
        result.Markdown.ShouldContain("```mermaid");
    }

    private static async Task<DocumentConverterResult> ConvertAsync(string fileName)
    {
        var markItDown = new MarkItDownClient();
        var path = TestAssetLoader.GetAssetPath(fileName);
        return await markItDown.ConvertAsync(path);
    }
}
