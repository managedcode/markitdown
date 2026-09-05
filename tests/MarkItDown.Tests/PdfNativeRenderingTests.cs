using SkiaSharp;

namespace MarkItDown.Tests;

public class PdfNativeRenderingTests
{
    [Fact]
    public void RenderRealPdf_EncodesPageAsPng()
    {
        using var source = File.OpenRead(TestAssetLoader.GetAssetPath(TestAssetCatalog.AutogenTrialTranscriptPdf));

        // This suite runs on the supported Windows, Linux and macOS desktop runtimes.
#pragma warning disable CA1416
        using var page = PDFtoImage.Conversion.ToImage(source);
#pragma warning restore CA1416
        Assert.True(page.Width > 0);
        Assert.True(page.Height > 0);

        using var png = page.Encode(SKEncodedImageFormat.Png, quality: 90);
        Assert.NotNull(png);
        Assert.True(png.Size > 8);
        Assert.True(png.Span[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
    }
}
