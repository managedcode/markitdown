using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using MarkItDown.Converters;
using Shouldly;

namespace MarkItDown.Tests;

public sealed class EpubConverterTests
{
    [Fact]
    public async Task ConvertAsync_StandardEpub_PreservesTitleMetadataAndChapters()
    {
        await using var source = TestAssetLoader.OpenAsset(TestAssetCatalog.AutogenEbookEpub);
        await using var result = await new EpubConverter().ConvertAsync(
            source, new StreamInfo(fileName: TestAssetCatalog.AutogenEbookEpub, extension: ".epub"));

        result.Title.ShouldBe("Test EPUB Document");
        result.Metadata[MetadataKeys.EpubTitle].ShouldBe("Test EPUB Document");
        result.Metadata[MetadataKeys.EpubAuthor].ShouldBe("Test Author");
        result.Markdown.ShouldContain("**Author:** Test Author");
        result.Markdown.ShouldContain("Chapter 1: Test Content");
        result.Markdown.ShouldContain("Chapter 2: More Content");
        result.Markdown.ShouldContain("This is a **test** paragraph");

        var chapters = result.Segments.Where(segment => segment.Type == SegmentType.Chapter).ToList();
        chapters.ShouldContain(segment => segment.Markdown.Contains("Chapter 1: Test Content", StringComparison.Ordinal));
        chapters.FindIndex(segment => segment.Markdown.Contains("Chapter 1: Test Content", StringComparison.Ordinal))
            .ShouldBeLessThan(chapters.FindIndex(segment => segment.Markdown.Contains("Chapter 2: More Content", StringComparison.Ordinal)));
        foreach (var segment in chapters)
        {
            segment.Number.ShouldNotBeNull();
            segment.AdditionalMetadata[MetadataKeys.Page].ShouldBe(segment.Number!.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConvertAsync_EpubContainerVariants_PreserveSpineOrder(bool prefixedNamespace)
    {
        var path = Path.Combine(Path.GetTempPath(), $"epub-spine-{Guid.NewGuid():N}.epub");
        try
        {
            File.Copy(TestAssetLoader.GetAssetPath(TestAssetCatalog.AutogenEbookEpub), path);
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                var container = archive.GetEntry("META-INF/container.xml")!;
                container.Delete();
                using (var writer = new StreamWriter(archive.CreateEntry("META-INF/container.xml").Open()))
                {
                    writer.Write(prefixedNamespace
                        ? "<c:container xmlns:c=\"urn:oasis:names:tc:opendocument:xmlns:container\"><c:rootfiles><c:rootfile full-path=\"EPUB/content.opf\"/></c:rootfiles></c:container>"
                        : "<container><rootfiles><rootfile full-path=\"EPUB/content.opf\"/></rootfiles></container>");
                }

                var package = archive.GetEntry("EPUB/content.opf")!;
                XDocument document;
                using (var packageStream = package.Open())
                {
                    document = XDocument.Load(packageStream);
                }

                XNamespace opf = "http://www.idpf.org/2007/opf";
                var spine = document.Root!.Element(opf + "spine")!;
                spine.ReplaceNodes(
                    new XElement(opf + "itemref", new XAttribute("idref", "chapter_1")),
                    new XElement(opf + "itemref", new XAttribute("idref", "chapter_0")));
                package.Delete();
                using var output = archive.CreateEntry("EPUB/content.opf").Open();
                document.Save(output);
            }

            await using var source = File.OpenRead(path);
            await using var result = await new EpubConverter().ConvertAsync(
                source, new StreamInfo(fileName: "spine.epub", extension: ".epub"));

            var chapters = result.Segments.Where(segment => segment.Type == SegmentType.Chapter).ToList();
            chapters.Count.ShouldBe(2);
            chapters[0].Markdown.ShouldContain("Chapter 2: More Content");
            chapters[1].Markdown.ShouldContain("Chapter 1: Test Content");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
