using System.Collections.Generic;
using MarkItDown;
using Shouldly;
using Xunit;

namespace MarkItDown.Tests.Segments;

public sealed class SegmentMarkdownComposerTests
{
    [Fact]
    public void Compose_SkipsCommentBlocks_WhenDerivingTitle()
    {
        var segments = new List<DocumentSegment>
        {
            new DocumentSegment("""
<!-- Image description:
Comment content that should not drive the title.
-->
![Placeholder](image.png)
""", SegmentType.Image),
            new DocumentSegment("# Actual Title\nBody", SegmentType.Page)
        };

        var streamInfo = new StreamInfo(fileName: "sample.docx");
        var result = SegmentMarkdownComposer.Compose(segments, new ConversionArtifacts(), streamInfo, SegmentOptions.Default);

        result.Title.ShouldBe("Actual Title");
    }

    [Fact]
    public void Compose_TruncatesTitleBeforeOrderedList()
    {
        var content = """Медичні протоколи та безпека Документ описує процедури реагування на захворювання, травми та надзвичайні ситуації у школі. 1. Щоденний моніторинг здоров'я • Температурний скринінг проводиться на вході в школу для усіх учнів та працівників.""";

        var segments = new List<DocumentSegment>
        {
            new DocumentSegment(content, SegmentType.Page)
        };

        var streamInfo = new StreamInfo(fileName: "sample.pdf");
        var result = SegmentMarkdownComposer.Compose(segments, new ConversionArtifacts(), streamInfo, SegmentOptions.Default);

        result.Title.ShouldBe("""Медичні протоколи та безпека Документ описує процедури реагування на захворювання, травми та надзвичайні ситуації у школі""");
    }

    public static IEnumerable<object[]> ConverterTitleNormalizationCases()
    {
        yield return new object[]
        {
            "PdfConverter",
            "Медичні протоколи та безпека. 1. Щоденний моніторинг здоров'я.",
            "Медичні протоколи та безпека"
        };

        yield return new object[]
        {
            "DocxConverter",
            "Звіт про виконання бюджету. • Ключові показники подано нижче.",
            "Звіт про виконання бюджету"
        };

        yield return new object[]
        {
            "PptxConverter",
            "Sales Overview: Fiscal 2025. 2) Regional breakdown follows.",
            "Sales Overview: Fiscal 2025"
        };

        yield return new object[]
        {
            "EpubConverter",
            "Chapter Introduction! ‣ Detailed timeline is provided in the appendix.",
            "Chapter Introduction"
        };

        yield return new object[]
        {
            "OdtConverter",
            "План уроку. ● Елементи курсу описано нижче.",
            "План уроку"
        };
    }

    [Theory]
    [MemberData(nameof(ConverterTitleNormalizationCases))]
    public void Compose_NormalizesTitlesForConverters(string converter, string content, string expectedTitle)
    {
        var segments = new List<DocumentSegment>
        {
            new DocumentSegment(content, SegmentType.Page)
        };

        var streamInfo = new StreamInfo(fileName: $"sample-{converter}.pdf");
        var result = SegmentMarkdownComposer.Compose(segments, new ConversionArtifacts(), streamInfo, SegmentOptions.Default);

        result.Title.ShouldBe(expectedTitle);
    }
}
