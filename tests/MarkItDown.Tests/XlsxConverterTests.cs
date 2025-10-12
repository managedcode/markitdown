using System;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;
using MarkItDown;
using MarkItDown.Converters;
using MarkItDown.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace MarkItDown.Tests;

public class XlsxConverterTests
{
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
