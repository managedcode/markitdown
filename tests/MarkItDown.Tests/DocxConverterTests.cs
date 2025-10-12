using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ManagedCode.MimeTypes;
using MarkItDown;
using MarkItDown.Converters;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using MarkItDown.Tests.Fixtures;
using Shouldly;

namespace MarkItDown.Tests;

public class DocxConverterTests
{
    [Fact]
    public async Task ConvertAsync_DocxWithImages_ExecutesPipelineAndCapturesArtifacts()
    {
        // Arrange
        var pipeline = new RecordingPipeline("DOCX ENRICHED");
        var converter = new DocxConverter(pipeline: pipeline);

        await using var stream = DocxInlineImageFactory.Create();
        var streamInfo = new StreamInfo(
            mimeType: MimeHelper.GetMimeType(".docx"),
            extension: ".docx",
            fileName: "doc-inline-image.docx");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        pipeline.Executed.ShouldBeTrue();
        result.Artifacts.Images.ShouldNotBeEmpty();
        result.Artifacts.TextBlocks.ShouldNotBeEmpty();

        var image = result.Artifacts.Images[0];
        image.SegmentIndex.ShouldNotBeNull();
        image.DetailedDescription.ShouldBe("DOCX ENRICHED");
        image.Metadata.ShouldContainKey(MetadataKeys.Page);
        image.PlaceholderMarkdown.ShouldNotBeNull();
        image.PlaceholderMarkdown!.ShouldStartWith("![");

        var segment = result.Segments[image.SegmentIndex!.Value];
        segment.Markdown.ShouldContain("DOCX ENRICHED");
        segment.Type.ShouldBe(SegmentType.Page);
        var placeholderIndex = segment.Markdown.IndexOf(image.PlaceholderMarkdown!, StringComparison.Ordinal);
        placeholderIndex.ShouldBeGreaterThanOrEqualTo(0);
        var trailing = segment.Markdown[(placeholderIndex + image.PlaceholderMarkdown!.Length)..];
        trailing.TrimStart('\r', '\n').ShouldStartWith("DOCX ENRICHED");
    }

    [Fact]
    public async Task ConvertAsync_DocxWithMergedCells_DuplicatesValuesAcrossRows()
    {
        using var source = new MemoryStream();
        using (var document = WordprocessingDocument.Create(source, WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            var table = new Table(
                new TableProperties(new TableWidth { Type = TableWidthUnitValues.Auto }),
                new TableGrid(new GridColumn(), new GridColumn(), new GridColumn(), new GridColumn()));

            table.Append(
                CreateRow("ITEM", "CONDITION", "QUANTITY", "LOCATION"),
                CreateMergedRow("Laptop", "Refurbished", "5", "Aisle 3", restart: true),
                CreateMergedRow(string.Empty, "Like-New (Open Box)", "3", "Aisle 3", restart: false),
                CreateMergedRow(string.Empty, "Used (Grade A)", "4", "Aisle 5", restart: false));

            mainPart.Document.Body!.Append(table);
            mainPart.Document.Save();

            TableRow CreateRow(params string[] cells)
            {
                var row = new TableRow();
                foreach (var cellText in cells)
                {
                    row.Append(new TableCell(new Paragraph(new Run(new Text(cellText ?? string.Empty)))));
                }

                return row;
            }

            TableRow CreateMergedRow(string item, string condition, string quantity, string location, bool restart)
            {
                var row = new TableRow();

                var mergeProps = new TableCellProperties
                {
                    VerticalMerge = new VerticalMerge { Val = restart ? MergedCellValues.Restart : MergedCellValues.Continue }
                };

                var itemCell = new TableCell(mergeProps);
                if (!string.IsNullOrWhiteSpace(item))
                {
                    itemCell.Append(new Paragraph(new Run(new Text(item))));
                }
                row.Append(itemCell);

                row.Append(new TableCell(new Paragraph(new Run(new Text(condition)))));
                row.Append(new TableCell(new Paragraph(new Run(new Text(quantity)))));
                row.Append(new TableCell(new Paragraph(new Run(new Text(location)))));

                return row;
            }
        }

        var bytes = source.ToArray();
        await using var docStream = new MemoryStream(bytes);
        var converter = new DocxConverter();
        var streamInfo = new StreamInfo(
            mimeType: MimeHelper.GetMimeType(".docx"),
            extension: ".docx",
            fileName: "merged-table.docx");

        var result = await converter.ConvertAsync(docStream, streamInfo);

        result.Markdown.ShouldContain("| Laptop | Refurbished | 5 | Aisle 3 |");
        result.Markdown.ShouldContain("| Laptop | Like-New (Open Box) | 3 | Aisle 3 |");
        result.Markdown.ShouldContain("| Laptop | Used (Grade A) | 4 | Aisle 5 |");
    }

    [Fact]
    public async Task ConvertAsync_DocxWithLegacyPicture_InvokesImageUnderstanding()
    {
        // Arrange
        var provider = new RecordingImageProvider();
        var converter = new DocxConverter(imageProvider: provider);

        await using var stream = DocxLegacyImageFactory.Create();
        var streamInfo = new StreamInfo(
            mimeType: MimeHelper.GetMimeType(".docx"),
            extension: ".docx",
            fileName: "doc-legacy-picture.docx");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        provider.CallCount.ShouldBe(1);

        var image = result.Artifacts.Images.ShouldHaveSingleItem();
        image.Metadata.ShouldContainKey(MetadataKeys.OcrText);
        image.Metadata[MetadataKeys.OcrText].ShouldBe("Legacy OCR text");
        image.RawText.ShouldBe("Legacy OCR text");
        image.Metadata.ShouldContainKey(MetadataKeys.Caption);
        image.Metadata[MetadataKeys.Caption].ShouldBe("Legacy caption");
    }

    [Fact]
    public async Task ConvertAsync_ComplexDocx_PreservesRichContent()
    {
        var client = new MarkItDownClient();
        var path = TestAssetLoader.GetAssetPath(TestAssetCatalog.ComplexDocx);

        var result = await client.ConvertAsync(path);

        result.Title.ShouldBe("Rich Text Formatting");
        result.Markdown.ShouldContain("https://example.com/docs");
        result.Markdown.ShouldContain("| Metric | Q1 | Q2 | Total |");
        result.Markdown.ShouldContain("Equation: x^2 + y^2 = z^2");
        result.Markdown.ShouldContain("• Bullet list item one");
        result.Markdown.ShouldContain("![");
    }

    [Fact]
    public async Task ConvertAsync_BrokenDocx_RaisesFileConversionError()
    {
        var client = new MarkItDownClient();
        var path = TestAssetLoader.GetAssetPath(TestAssetCatalog.BrokenDocx);

        var exception = await Should.ThrowAsync<UnsupportedFormatException>(async () => await client.ConvertAsync(path));
        exception.InnerException.ShouldNotBeNull();
        exception.InnerException.ShouldBeOfType<AggregateException>();
        var aggregate = (AggregateException)exception.InnerException!;
        aggregate.InnerExceptions.ShouldContain(e => e is FileConversionException);
    }

    [Fact]
    public async Task ConvertAsync_SpecimenManualDocx_ExtractsKeySections()
    {
        const string fileName = "CLI.CST.MAN.001 V5 - SPECIMEN SUBMISSION MANUAL.docx";
        var candidate = Path.Combine(TestAssetLoader.AssetsDirectory, fileName);
        if (!File.Exists(candidate))
        {
            return;
        }

        var client = new MarkItDownClient();
        var path = TestAssetLoader.GetAssetPath(fileName);

        var result = await client.ConvertAsync(path);

        result.Markdown.ShouldContain("Specimen Submission Manual");
        result.Markdown.ShouldContain("Title 17 Specimen Submission Requirements");
        result.Markdown.ShouldContain("Specimen Submission Instructions");

        result.Metadata.ShouldContainKey(MetadataKeys.DocumentPages);
        result.Metadata[MetadataKeys.DocumentPages].ShouldBe("26");
        result.Segments.Count.ShouldBe(26);

        for (var i = 0; i < result.Segments.Count; i++)
        {
            var segment = result.Segments[i];
            segment.Type.ShouldBe(SegmentType.Page);
            segment.Number.ShouldBe(i + 1);
            segment.AdditionalMetadata.ShouldContainKey(MetadataKeys.Page);
            segment.AdditionalMetadata[MetadataKeys.Page].ShouldBe((i + 1).ToString(CultureInfo.InvariantCulture));
        }

        result.Markdown.ShouldContain("Do not refrigerate or freeze.<br />Specimens received > 16 hours after collection may be rejected<br />Do not collect in red-top, EDTA, or ACD tube.");
        result.Markdown.ShouldContain("-Shipping containers available from the lab");
        result.Markdown.ShouldContain("| Culture AFB | Wound or Abscess samples |");
        result.Markdown.ShouldContain("| GeneXpert MTB/RIF NAT |");
        result.Markdown.ShouldNotContain("Transport ASAP at ambient temperatureTransport ASAP");
        result.Markdown.ShouldContain("<!-- Table 2 continues on page 2 (pages 1-4) -->");

        var mycobacteriologyTable = result.Artifacts.Tables
            .FirstOrDefault(table => table.Rows.Count > 0 &&
                                      table.Rows[0].Count >= 6 &&
                                      table.Rows[0][0].Equals("TEST NAME", StringComparison.OrdinalIgnoreCase) &&
                                      table.Rows.Any(row => row.Any(cell => cell.Contains("GeneXpert", StringComparison.OrdinalIgnoreCase))));

        mycobacteriologyTable.ShouldNotBeNull();
        mycobacteriologyTable!.Metadata.ShouldContainKey(MetadataKeys.TablePageRange);
        mycobacteriologyTable.Metadata[MetadataKeys.TablePageRange].ShouldBe("15-16");
        mycobacteriologyTable.Metadata.ShouldContainKey(MetadataKeys.TablePageStart);
        mycobacteriologyTable.Metadata[MetadataKeys.TablePageStart].ShouldBe("15");
    }

    private sealed class RecordingImageProvider : IImageUnderstandingProvider
    {
        public int CallCount { get; private set; }

        public Task<ImageUnderstandingResult?> AnalyzeAsync(Stream stream, StreamInfo streamInfo, ImageUnderstandingRequest? request = null, CancellationToken cancellationToken = default)
        {
            CallCount++;

            var metadata = new Dictionary<string, string>
            {
                { "provider", "recording" }
            };

            var result = new ImageUnderstandingResult(
                caption: "Legacy caption",
                text: "Legacy OCR text",
                tags: Array.Empty<string>(),
                detectedObjects: Array.Empty<string>(),
                metadata: metadata);

            return Task.FromResult<ImageUnderstandingResult?>(result);
        }
    }
}
