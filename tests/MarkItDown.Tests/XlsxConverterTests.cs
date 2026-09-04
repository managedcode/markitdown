using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ManagedCode.MimeTypes;
using MarkItDown;
using MarkItDown.Converters;
using MarkItDown.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace MarkItDown.Tests;

public class XlsxConverterTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public async Task ConvertAsync_XlsxWithMultilineCells_KeepsMarkdownRowsIntact(string lineBreak)
    {
        var path = Path.Combine(Path.GetTempPath(), $"multiline-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
            {
                var workbook = document.AddWorkbookPart();
                var worksheet = workbook.AddNewPart<WorksheetPart>();
                var sharedStrings = workbook.AddNewPart<SharedStringTablePart>();
                sharedStrings.SharedStringTable = new SharedStringTable(new SharedStringItem(new Text($"Shared{lineBreak}value|tail")));
                worksheet.Worksheet = new Worksheet(new SheetData(
                    new Row(
                        new Cell { CellReference = "A1", DataType = CellValues.InlineString, InlineString = new InlineString(new Text("Inline")) },
                        new Cell { CellReference = "B1", DataType = CellValues.InlineString, InlineString = new InlineString(new Text("Shared")) })
                    { RowIndex = 1 },
                    new Row(
                        new Cell { CellReference = "A2", DataType = CellValues.InlineString, InlineString = new InlineString(new Text($"First{lineBreak}second|tail")) },
                        new Cell { CellReference = "B2", DataType = CellValues.SharedString, CellValue = new CellValue("0") })
                    { RowIndex = 2 }));
                workbook.Workbook = new Workbook(new Sheets(new Sheet
                {
                    Id = workbook.GetIdOfPart(worksheet),
                    SheetId = 1,
                    Name = "Multiline"
                }));
                workbook.Workbook.Save();
            }

            await using var source = File.OpenRead(path);
            await using var result = await new XlsxConverter().ConvertAsync(
                source, new StreamInfo(fileName: "multiline.xlsx", extension: ".xlsx"));

            result.Markdown.ShouldContain("| First<br />second\\|tail | Shared<br />value\\|tail |");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConvertAsync_ComplexXlsx_RendersSheetsAndFormulas()
    {
        var client = new MarkItDownClient();
        var path = TestAssetLoader.GetAssetPath(TestAssetCatalog.ComplexXlsx);

        var result = await client.ConvertAsync(path);

        result.Title.ShouldBe("complex");
        result.Markdown.ShouldContain("## Summary");
        result.Markdown.ShouldContain("| Month | Bookings | Expenses | Net | Notes |");
        result.Markdown.ShouldContain("=AVERAGE(D2:D7)");
        result.Markdown.ShouldContain("Detailed Pipeline");
        result.Markdown.ShouldContain("Fabrikam");
        result.Markdown.ShouldContain("## Coverage");
        result.Markdown.ShouldContain("2024-01-01");
        result.Markdown.ShouldContain("TRUE");
    }

    [Fact]
    public async Task ConvertAsync_XlsxWithMergedCells_DuplicatesValuesAcrossRows()
    {
        using var memory = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(memory, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = document.AddWorkbookPart();
            var workbook = new Workbook();
            var sheets = workbook.AppendChild(new Sheets());

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);

            worksheetPart.Worksheet.AppendChild(new MergeCells(new MergeCell { Reference = new StringValue("A2:A4") }));

            sheetData.AppendChild(CreateRow(1, ("A", "ITEM"), ("B", "CONDITION"), ("C", "QUANTITY"), ("D", "LOCATION")));
            sheetData.AppendChild(CreateRow(2, ("A", "Laptop"), ("B", "Refurbished"), ("C", "5"), ("D", "Aisle 3")));
            sheetData.AppendChild(CreateRow(3, ("B", "Like-New (Open Box)"), ("C", "3"), ("D", "Aisle 3")));
            sheetData.AppendChild(CreateRow(4, ("B", "Used (Grade A)"), ("C", "4"), ("D", "Aisle 5")));

            sheets.AppendChild(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Inventory"
            });

            workbookPart.Workbook = workbook;
            workbookPart.Workbook.Save();

            Row CreateRow(uint rowIndex, params (string Column, string Value)[] cells)
            {
                var row = new Row { RowIndex = rowIndex };
                foreach (var (column, value) in cells)
                {
                    var cell = new Cell
                    {
                        CellReference = new StringValue(column + rowIndex.ToString(CultureInfo.InvariantCulture)),
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new Text(value))
                    };
                    row.Append(cell);
                }

                return row;
            }
        }

        var bytes = memory.ToArray();
        await using var stream = new MemoryStream(bytes);
        var converter = new XlsxConverter();
        var streamInfo = new StreamInfo(
            mimeType: MimeHelper.GetMimeType(".xlsx"),
            extension: ".xlsx",
            fileName: "merged.xlsx");

        var result = await converter.ConvertAsync(stream, streamInfo);

        result.Markdown.ShouldContain("| ITEM | CONDITION | QUANTITY | LOCATION |");
        result.Markdown.ShouldContain("| Laptop | Refurbished | 5 | Aisle 3 |");
        result.Markdown.ShouldContain("| Laptop | Like-New (Open Box) | 3 | Aisle 3 |");
        result.Markdown.ShouldContain("| Laptop | Used (Grade A) | 4 | Aisle 5 |");
    }

    [Fact]
    public void AcceptsInput_RecognizesMimeTypeWithoutExtension()
    {
        var converter = new XlsxConverter();
        var streamInfo = new StreamInfo(mimeType: MimeHelper.GetMimeType(".xlsx"), extension: null);

        converter.AcceptsInput(streamInfo).ShouldBeTrue();
    }

    [Fact]
    public async Task ConvertAsync_BrokenXlsx_RaisesFileConversionError()
    {
        var client = new MarkItDownClient();
        var path = TestAssetLoader.GetAssetPath(TestAssetCatalog.BrokenXlsx);

        var exception = await Should.ThrowAsync<UnsupportedFormatException>(async () => await client.ConvertAsync(path));
        exception.InnerException.ShouldNotBeNull();
        exception.InnerException.ShouldBeOfType<AggregateException>();
        var aggregate = (AggregateException)exception.InnerException!;
        aggregate.InnerExceptions.ShouldContain(e => e is FileConversionException);
    }

    [Fact]
    public async Task ConvertAsync_EmptyXlsx_ProducesNoDataMessage()
    {
        var client = new MarkItDownClient();
        var path = TestAssetLoader.GetAssetPath(TestAssetCatalog.EmptyXlsx);

        var result = await client.ConvertAsync(path);

        result.Markdown.ShouldContain("*No data found*");
    }
}
