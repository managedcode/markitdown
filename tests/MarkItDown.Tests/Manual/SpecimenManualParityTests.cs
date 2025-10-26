using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ManagedCode.MimeTypes;
using MarkItDown.Converters;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using Shouldly;
using Xunit.Abstractions;

namespace MarkItDown.Tests.Manual;

public class SpecimenManualParityTests(ITestOutputHelper output)
{
    private const string PdfAsset = "TestAssetCatalog.CLICSTMAN001V5SpecimenSubmissionManualPdf";
    private const string DocxAsset = "TestAssetCatalog.CLICSTMAN001V5SpecimenSubmissionManualDocx";

    [Fact]
    public async Task PdfAndDocx_ProduceMatchingTextAndTables()
    {
        var pdfPath = TryGetAssetPath(PdfAsset);
        var docxPath = TryGetAssetPath(DocxAsset);

        if (pdfPath is null || docxPath is null)
        {
            return;
        }

        var client = new MarkItDownClient();
        var docxResult = await client.ConvertAsync(docxPath);
        output.WriteLine("DOCX table metadata sample: " + string.Join(", ", docxResult.Artifacts.Tables[0].Metadata.Select(pair => $"{pair.Key}={pair.Value}")));

        var intelligenceResult = BuildDocumentIntelligenceResult(docxResult);
        output.WriteLine("Synthetic table metadata: " + string.Join(", ", intelligenceResult.Tables[0].Metadata.Select(pair => $"{pair.Key}={pair.Value}")));
        var provider = new StubDocumentIntelligenceProvider(intelligenceResult);
        var converter = new PdfConverter(documentProvider: provider);

        await using var pdfStream = File.OpenRead(pdfPath);
        var streamInfo = new StreamInfo(
            mimeType: MimeHelper.PDF,
            extension: ".pdf",
            fileName: Path.GetFileName(pdfPath),
            localPath: pdfPath);

        var pdfResult = await converter.ConvertAsync(pdfStream, streamInfo);
        output.WriteLine("PDF table metadata sample: " + string.Join(", ", pdfResult.Artifacts.Tables[0].Metadata.Select(pair => $"{pair.Key}={pair.Value}")));

        var docxPages = docxResult.Segments.Where(s => s.Type == SegmentType.Page).OrderBy(s => s.Number).ToList();
        var pdfPages = pdfResult.Segments.Where(s => s.Type == SegmentType.Page).OrderBy(s => s.Number).ToList();

        pdfPages.Count.ShouldBe(docxPages.Count);

        for (var i = 0; i < docxPages.Count; i++)
        {
            var expected = NormalizePage(docxPages[i].Markdown);
            var actual = NormalizePage(pdfPages[i].Markdown);
            output.WriteLine($"Page {i + 1} expected:\n{expected}\n");
            output.WriteLine($"Page {i + 1} actual:\n{actual}\n");
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                output.WriteLine($"Lengths -> expected: {expected.Length}, actual: {actual.Length}");
                var prefix = Path.Combine(Path.GetTempPath(), $"specimen_parity_page_{i + 1}");
                File.WriteAllText(prefix + "_expected.txt", expected);
                File.WriteAllText(prefix + "_actual.txt", actual);
            }
            actual.ShouldBe(expected, $"Mismatch in page {i + 1}");
        }

        pdfResult.Artifacts.Tables.Count.ShouldBe(docxResult.Artifacts.Tables.Count);

        for (var i = 0; i < docxResult.Artifacts.Tables.Count; i++)
        {
            var expectedTable = docxResult.Artifacts.Tables[i].Rows;
            var actualTable = pdfResult.Artifacts.Tables[i].Rows;

            actualTable.Count.ShouldBe(expectedTable.Count, $"Row count mismatch in table {i + 1}");

            for (var r = 0; r < expectedTable.Count; r++)
            {
                actualTable[r].ShouldBe(expectedTable[r], $"Mismatch in table {i + 1}, row {r + 1}");
            }
        }
    }

    [Fact]
    public async Task PdfAndDocx_EndToEndParity()
    {
        var pdfPath = TryGetAssetPath(PdfAsset);
        var docxPath = TryGetAssetPath(DocxAsset);

        if (pdfPath is null || docxPath is null)
        {
            return;
        }

        var docxClient = new MarkItDownClient();
        var docxResult = await docxClient.ConvertAsync(docxPath);

        var intelligenceResult = BuildDocumentIntelligenceResult(docxResult);
        var options = new MarkItDownOptions
        {
            DocumentIntelligenceProvider = new StubDocumentIntelligenceProvider(intelligenceResult)
        };

        var pdfClient = new MarkItDownClient(options);
        var pdfResult = await pdfClient.ConvertAsync(pdfPath);

        var docxPages = docxResult.Segments.Where(s => s.Type == SegmentType.Page).OrderBy(s => s.Number).ToList();
        var pdfPages = pdfResult.Segments.Where(s => s.Type == SegmentType.Page).OrderBy(s => s.Number).ToList();

        pdfPages.Count.ShouldBe(docxPages.Count);

        for (var i = 0; i < docxPages.Count; i++)
        {
            var expected = NormalizePage(docxPages[i].Markdown);
            var actual = NormalizePage(pdfPages[i].Markdown);
            actual.ShouldBe(expected, $"Mismatch in page {i + 1} end-to-end parity test");
        }

        pdfResult.Artifacts.Tables.Count.ShouldBe(docxResult.Artifacts.Tables.Count);

        for (var i = 0; i < docxResult.Artifacts.Tables.Count; i++)
        {
            var expectedTable = docxResult.Artifacts.Tables[i].Rows;
            var actualTable = pdfResult.Artifacts.Tables[i].Rows;

            actualTable.Count.ShouldBe(expectedTable.Count, $"Row count mismatch in table {i + 1} (end-to-end)");

            for (var r = 0; r < expectedTable.Count; r++)
            {
                actualTable[r].ShouldBe(expectedTable[r], $"Mismatch in table {i + 1}, row {r + 1} (end-to-end)");
            }
        }
    }

    private static string NormalizePage(string markdown)
    {
        var lines = markdown.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var normalized = new List<string>();
        var insideComment = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.StartsWith("<!--", StringComparison.Ordinal))
            {
                if (!line.Contains("-->", StringComparison.Ordinal))
                {
                    insideComment = true;
                }
                continue;
            }

            if (insideComment)
            {
                if (line.Contains("-->", StringComparison.Ordinal))
                {
                    insideComment = false;
                }
                continue;
            }

            if (line.StartsWith("![", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.Length > 0)
            {
                var collapsed = Regex.Replace(line, "\\s+", " ");
                normalized.Add(collapsed);
            }
        }

        var text = string.Join("\n", normalized);
        return text.Replace("<br />", " ", StringComparison.OrdinalIgnoreCase);
    }

    private static DocumentIntelligenceResult BuildDocumentIntelligenceResult(DocumentConverterResult docxResult)
    {
        var tableComments = MapTableComments(docxResult.Markdown);

        var tables = docxResult.Artifacts.Tables
            .Select((table, index) =>
            {
                var pageNumber = table.PageNumber ?? 1;
                var rows = table.Rows.Select(row => (IReadOnlyList<string>)row.ToList()).ToList();

                var metadata = new Dictionary<string, string>();
                foreach (var pair in table.Metadata)
                {
                    metadata[pair.Key] = pair.Value;
                }

                metadata[MetadataKeys.TableIndex] = (index + 1).ToString(CultureInfo.InvariantCulture);

                if (tableComments.TryGetValue(index, out var comment))
                {
                    metadata[MetadataKeys.TableComment] = comment;
                }

                return new DocumentTableResult(
                    pageNumber,
                    rows,
                    metadata);
            })
            .ToList();

        var tablesByPage = new Dictionary<int, List<int>>();
        for (var index = 0; index < tables.Count; index++)
        {
            var table = tables[index];
            var pageStart = table.Metadata.TryGetValue(MetadataKeys.TablePageStart, out var startValue) && int.TryParse(startValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStart)
                ? parsedStart
                : table.PageNumber;

            var pageEnd = table.Metadata.TryGetValue(MetadataKeys.TablePageEnd, out var endValue) && int.TryParse(endValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedEnd)
                ? parsedEnd
                : pageStart;

            if (pageEnd < pageStart)
            {
                pageEnd = pageStart;
            }

            for (var page = pageStart; page <= pageEnd; page++)
            {
                if (!tablesByPage.TryGetValue(page, out var list))
                {
                    list = new List<int>();
                    tablesByPage[page] = list;
                }

                list.Add(index);
            }
        }

        var pages = new List<DocumentPageResult>();
        foreach (var segment in docxResult.Segments.Where(s => s.Type == SegmentType.Page))
        {
            if (!segment.Number.HasValue)
            {
                continue;
            }

            var pageNumber = segment.Number.Value;
            tablesByPage.TryGetValue(pageNumber, out var indices);
            var tableIndices = indices is not null ? indices.ToArray() : Array.Empty<int>();
            var pageText = StripTables(segment.Markdown, tableIndices, out var consumedIndices);
            var consumedArray = consumedIndices.Count > 0 ? consumedIndices.OrderBy(static value => value).ToArray() : Array.Empty<int>();
            pages.Add(new DocumentPageResult(
                pageNumber,
                pageText,
                tableIndices: consumedArray));
        }

        var images = new List<DocumentImageResult>();
        foreach (var image in docxResult.Artifacts.Images)
        {
            if (image.FilePath is null || image.PageNumber is null)
            {
                continue;
            }

            var bytes = File.ReadAllBytes(image.FilePath);
            images.Add(new DocumentImageResult(
                image.PageNumber.Value,
                bytes,
                image.ContentType ?? MimeHelper.PNG,
                caption: image.Label,
                metadata: new Dictionary<string, string>(image.Metadata)));
        }

        return new DocumentIntelligenceResult(pages, tables, images);
    }

    private static Dictionary<int, string> MapTableComments(string markdown)
    {
        var mapping = new Dictionary<int, string>();
        var pendingComments = new Queue<string>();
        var lines = markdown.Split(new[] { '\r', '\n' }, StringSplitOptions.None);

        var tableIndex = -1;
        var insideTable = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                if (!insideTable)
                {
                    continue;
                }
            }

            if (line.StartsWith("<!-- Table spans", StringComparison.OrdinalIgnoreCase))
            {
                pendingComments.Enqueue(line);
                continue;
            }

            if (line.StartsWith("|", StringComparison.Ordinal))
            {
                if (!insideTable)
                {
                    insideTable = true;
                    tableIndex++;

                    if (pendingComments.Count > 0 && !mapping.ContainsKey(tableIndex))
                    {
                        mapping[tableIndex] = pendingComments.Dequeue();
                    }
                }

                continue;
            }

            if (insideTable)
            {
                insideTable = false;
            }
        }

        return mapping;
    }

    private static string StripTables(string markdown, IReadOnlyList<int> tableIndices, out HashSet<int> consumedIndices)
    {
        using var reader = new StringReader(markdown);
        var builder = new StringBuilder();
        var tablePointer = 0;
        var insideTable = false;
        consumedIndices = new HashSet<int>();

        while (true)
        {
            var rawLine = reader.ReadLine();
            if (rawLine is null)
            {
                break;
            }

            var line = rawLine.TrimEnd();

            if (line.StartsWith("<!-- Table", StringComparison.OrdinalIgnoreCase))
            {
                insideTable = false;
                if (line.IndexOf("continues", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AppendLine(builder, line);
                }
                continue;
            }

            if (line.StartsWith("|", StringComparison.Ordinal))
            {
                if (!insideTable)
                {
                    insideTable = true;
                    if (tablePointer < tableIndices.Count)
                    {
                        var index = tableIndices[tablePointer++];
                        consumedIndices.Add(index);
                        AppendLine(builder, $"{{{{TABLE:{index}}}}}");
                    }
                }

                continue;
            }

            insideTable = false;
            AppendLine(builder, line.Trim());
        }

        return builder.ToString();
    }

    private static void AppendLine(StringBuilder builder, string line)
    {
        if (line.Length == 0)
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.Append(line);
    }

    private static string? TryGetAssetPath(string assetName)
    {
        var candidate = Path.Combine(TestAssetLoader.AssetsDirectory, assetName);
        if (!File.Exists(candidate))
        {
            return null;
        }

        return TestAssetLoader.GetAssetPath(assetName);
    }

    private sealed class StubDocumentIntelligenceProvider : IDocumentIntelligenceProvider
    {
        private readonly DocumentIntelligenceResult result;

        public StubDocumentIntelligenceProvider(DocumentIntelligenceResult result)
        {
            this.result = result;
        }

        public Task<DocumentIntelligenceResult?> AnalyzeAsync(Stream stream, StreamInfo streamInfo, DocumentIntelligenceRequest? request = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<DocumentIntelligenceResult?>(result);
        }
    }
}
