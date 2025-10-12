using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MarkItDown;
using MarkItDown.Converters;
using MarkItDown.Tests;
using MarkItDown.Intelligence.Configuration;
using MarkItDown.Intelligence.Providers.Azure;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace MarkItDown.Tests.Manual;

public class ManualConversionDebugTests(ITestOutputHelper output)
{
    public static TheoryData<string, string> Assets => new()
    {
        // { TestAssetCatalog.CLICSTMAN001V5SpecimenSubmissionManualPdf, "Specimen submission manual PDF" },
        // { TestAssetCatalog.CLICSTMAN001V5SpecimenSubmissionManualDocx, "Specimen submission manual DOCX" },
        // { TestAssetCatalog.LWP88AdminDashboardUserGuideDocx, "Admin dashboard user guide DOCX" },
        // { TestAssetCatalog.UserGuideV14Docx, "User guide v1.4 DOCX" }
    };

    [Theory(Skip = "manuak")]
    [MemberData(nameof(Assets))]
    public async Task ConvertAndDumpSegmentsAsync(string assetName, string description)
    {
        MarkItDownOptions options;
        try
        {
            options = CreateOptions();
        }
        catch (InvalidOperationException ex)
        {
            output.WriteLine($"Skipping manual conversion: {ex.Message}");
            return;
        }
        var candidate = Path.Combine(TestAssetLoader.AssetsDirectory, assetName);
        if (!File.Exists(candidate))
        {
            output.WriteLine($"Skipping {assetName} because the fixture is not present.");
            return;
        }

        var client = new MarkItDownClient(options);
        var path = TestAssetLoader.GetAssetPath(assetName);

        output.WriteLine($"Asset: {assetName}");
        output.WriteLine($"Description: {description}");
        output.WriteLine($"Resolved path: {path}");
        output.WriteLine($"Azure Document Intelligence: {(options.AzureIntelligence?.DocumentIntelligence is null ? "disabled" : "enabled")}");
        output.WriteLine($"Azure Vision: {(options.AzureIntelligence?.Vision is null ? "disabled" : "enabled")}");
        output.WriteLine($"AI image enrichment: {(options.EnableAiImageEnrichment ? "enabled" : "disabled")}");
        output.WriteLine(string.Empty);

        var scenarios = BuildScenarios(assetName);

        foreach (var scenario in scenarios)
        {
            output.WriteLine($"Scenario: {scenario.Description}");

            var progressReporter = new Progress<ConversionProgress>(info =>
            {
                var prefix = string.IsNullOrWhiteSpace(scenario.NameSuffix) ? "default" : scenario.NameSuffix;
                output.WriteLine($"[{prefix}] Progress: {info.Stage} {info.Completed}/{info.Total} {info.Details}");
            });

            DocumentConverterResult result;
            if (scenario.Configure is null)
            {
                result = await client.ConvertAsync(path, progressReporter);
            }
            else
            {
                var request = ConversionRequest.FromConfiguration(builder =>
                {
                    scenario.Configure(builder);
                });

                result = await client.ConvertAsync(path, progressReporter, request);
            }

            var outputPath = await PersistResultAsync(assetName, result, scenario.NameSuffix);

            AssertSegmentsSequential(result.Segments);
            AssertImagesPersisted(result.Artifacts.Images, result.Segments.Count);

            var recomposed = SegmentMarkdownComposer.Compose(
                result.Segments,
                result.Artifacts,
                new StreamInfo(fileName: Path.GetFileName(path)),
                options.Segments,
                result.Title,
                result.GeneratedAtUtc);

            Assert.Equal(ExtractBody(recomposed.Markdown), ExtractBody(result.Markdown));

            output.WriteLine($"[{scenario.Description}] Title: {result.Title ?? "<none>"}");
            output.WriteLine($"[{scenario.Description}] Segment count: {result.Segments.Count}");
            output.WriteLine($"[{scenario.Description}] Image artifacts: {result.Artifacts.Images.Count}");
            output.WriteLine($"[{scenario.Description}] Text block artifacts: {result.Artifacts.TextBlocks.Count}");
            output.WriteLine(new string('-', 80));

            var missingDescriptions = new List<string>();

            for (var i = 0; i < result.Artifacts.Images.Count; i++)
            {
                var image = result.Artifacts.Images[i];
                output.WriteLine($"[{scenario.Description}] Image #{i + 1} [{image.Label ?? "unlabeled"}]");
                var resolvedDescription = ResolveDescription(image);
                if (string.IsNullOrWhiteSpace(resolvedDescription))
                {
                    string fallbackLabel = image.Label ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(fallbackLabel) &&
                        image.Metadata.TryGetValue(MetadataKeys.Page, out var pageValue) &&
                        !string.IsNullOrWhiteSpace(pageValue))
                    {
                        fallbackLabel = $"page {pageValue.Trim()}";
                    }

                    if (string.IsNullOrWhiteSpace(fallbackLabel))
                    {
                        fallbackLabel = $"#{i + 1}";
                    }

                    missingDescriptions.Add(fallbackLabel);
                }

                output.WriteLine($"- Detailed description: {resolvedDescription ?? "<none>"}");
                output.WriteLine($"- Placeholder: {image.PlaceholderMarkdown ?? "<none>"}");
                output.WriteLine($"- Mermaid: {image.MermaidDiagram ?? "<none>"}");
                output.WriteLine($"- OCR: {image.RawText ?? "<none>"}");
                AssertDescriptionInMarkdown(image, resolvedDescription, result.Markdown);
                if (image.Metadata.Count > 0)
                {
                    output.WriteLine("- Metadata:");
                    foreach (var kvp in image.Metadata)
                    {
                        output.WriteLine($"  * {kvp.Key}: {kvp.Value}");
                    }
                }

                output.WriteLine(new string('-', 80));
            }

            Assert.True(
                missingDescriptions.Count == 0,
                $"[{scenario.Description}] Images missing AI descriptions: {string.Join(", ", missingDescriptions)}");

            for (var i = 0; i < result.Segments.Count; i++)
            {
                var segment = result.Segments[i];
                output.WriteLine($"[{scenario.Description}] Segment #{i} [{segment.Type}]");
                output.WriteLine(segment.Markdown);
                output.WriteLine(new string('-', 80));
            }

            output.WriteLine($"[{scenario.Description}] MetaMD output written to: {outputPath}");
            ValidateMetaMarkdown(result);
        }
    }

    private static IReadOnlyList<ConversionScenario> BuildScenarios(string assetName)
    {
        if (!assetName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
            new ConversionScenario("Default pipeline", null, null)
            };
        }

        return new[]
        {
            new ConversionScenario(
                "Structured PDF parsing",
                "structured",
                builder =>
                {
                    builder.UsePdfConversionMode(PdfConversionMode.Structured);
                    builder.ConfigureSegments(segments => segments with
                    {
                        Image = segments.Image with { EnableImageUnderstandingProvider = true }
                    });
                }),
            new ConversionScenario(
                "Rendered pages with OCR",
                "ocr",
                builder =>
                {
                    builder.UsePdfConversionMode(PdfConversionMode.RenderedPageOcr);
                    builder.ConfigureSegments(segments => segments with
                    {
                        Image = segments.Image with { EnableImageUnderstandingProvider = true }
                    });
                })
        };
    }

    [Fact(Skip = "manual")]
    public async Task SpecimenManual_PdfAndDocx_ProduceComparableOutput()
    {
        MarkItDownOptions options;
        try
        {
            options = CreateOptions();
        }
        catch (InvalidOperationException ex)
        {
            output.WriteLine($"Skipping comparison: {ex.Message}");
            return;
        }

        var pdfAsset = "TestAssetCatalog.CLICSTMAN001V5SpecimenSubmissionManualPdf";
        var docxAsset = "TestAssetCatalog.CLICSTMAN001V5SpecimenSubmissionManualDocx";

        var pdfCandidate = Path.Combine(TestAssetLoader.AssetsDirectory, pdfAsset);
        var docxCandidate = Path.Combine(TestAssetLoader.AssetsDirectory, docxAsset);
        if (!File.Exists(pdfCandidate) || !File.Exists(docxCandidate))
        {
            output.WriteLine("Skipping comparison because one or more assets are missing.");
            return;
        }

        var client = new MarkItDownClient(options);
        var pdfPath = TestAssetLoader.GetAssetPath(pdfAsset);
        var docxPath = TestAssetLoader.GetAssetPath(docxAsset);

        var pdfRequest = ConversionRequest.FromConfiguration(builder =>
        {
            builder.UsePdfConversionMode(PdfConversionMode.Structured);
            builder.ConfigureSegments(segments => segments with
            {
                Image = segments.Image with { EnableImageUnderstandingProvider = true }
            });
        });

        var pdfResult = await client.ConvertAsync(pdfPath, pdfRequest);
        var docxResult = await client.ConvertAsync(docxPath);

        await PersistResultAsync(pdfAsset, pdfResult, "structured-comparison");
        await PersistResultAsync(docxAsset, docxResult, "structured-comparison");

        var pdfBody = ExtractBody(pdfResult.Markdown);
        var docxBody = ExtractBody(docxResult.Markdown);

        var pdfWords = ExtractDistinctWords(pdfBody);
        var docxWords = ExtractDistinctWords(docxBody);
        var shared = pdfWords.Intersect(docxWords, StringComparer.OrdinalIgnoreCase).Count();
        var minPopulation = Math.Max(1, Math.Min(pdfWords.Count, docxWords.Count));
        var overlapRatio = shared / (double)minPopulation;

        output.WriteLine($"PDF distinct words: {pdfWords.Count}");
        output.WriteLine($"DOCX distinct words: {docxWords.Count}");
        output.WriteLine($"Shared words: {shared}");
        output.WriteLine($"Overlap ratio: {overlapRatio:P2}");

        Assert.True(shared >= 200, $"Expected at least 200 shared words between PDF and DOCX outputs (found {shared}).");
        Assert.True(overlapRatio >= 0.35, $"Expected at least 35% overlap between the smaller vocabulary and the shared words (ratio was {overlapRatio:P2}).");

        var normalizedPdf = NormalizeMarkdownForComparison(pdfResult.Markdown);
        var normalizedDocx = NormalizeMarkdownForComparison(docxResult.Markdown);

        var pdfOnly = normalizedPdf.Except(normalizedDocx, StringComparer.OrdinalIgnoreCase).ToList();
        var docxOnly = normalizedDocx.Except(normalizedPdf, StringComparer.OrdinalIgnoreCase).ToList();

        output.WriteLine($"PDF-only normalized lines: {pdfOnly.Count}");
        output.WriteLine($"DOCX-only normalized lines: {docxOnly.Count}");
        Assert.Contains("Specimen Submission Manual", pdfBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Specimen Submission Manual", docxBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "manual")]
    public async Task SpecimenManual_PdfAsImages_RunsFullDocumentOcr()
    {
        MarkItDownOptions options;
        try
        {
            options = CreateOptions();
        }
        catch (InvalidOperationException ex)
        {
            output.WriteLine($"Skipping OCR scenario: {ex.Message}");
            return;
        }

        var pdfAsset = "TestAssetCatalog.CLICSTMAN001V5SpecimenSubmissionManualPdf";
        var pdfCandidate = Path.Combine(TestAssetLoader.AssetsDirectory, pdfAsset);
        if (!File.Exists(pdfCandidate))
        {
            output.WriteLine("Skipping OCR scenario because the PDF asset is missing.");
            return;
        }

        var client = new MarkItDownClient(options);
        var pdfPath = TestAssetLoader.GetAssetPath(pdfAsset);

        var ocrRequest = ConversionRequest.FromConfiguration(builder =>
        {
            builder.UsePdfConversionMode(PdfConversionMode.RenderedPageOcr);
            builder.ConfigureSegments(segments => segments with
            {
                Image = segments.Image with { EnableImageUnderstandingProvider = true }
            });
        });

        var result = await client.ConvertAsync(pdfPath, (IProgress<ConversionProgress>?)null, ocrRequest);
        await PersistResultAsync(pdfAsset, result, "rendered-ocr");

        Assert.NotEmpty(result.Segments);
        Assert.True(result.Artifacts.Images.Count >= result.Segments.Count, "Expected an image artifact for each rendered page.");
        Assert.All(result.Segments, segment => Assert.Contains("![", segment.Markdown, StringComparison.Ordinal));

        var ocrHits = result.Artifacts.Images.Count(image =>
            !string.IsNullOrWhiteSpace(image.RawText) ||
            (image.Metadata.TryGetValue(MetadataKeys.OcrText, out var text) && !string.IsNullOrWhiteSpace(text)));
        Assert.True(ocrHits > 0, "Expected at least one image artifact to produce OCR text.");

        Assert.Contains("Riverside University Health System", result.Markdown, StringComparison.OrdinalIgnoreCase);
        ValidateMetaMarkdown(result);
    }

    [Fact(Skip = "manual")]
    public async Task SpecimenManual_PdfStructured_WithImageRecognition()
    {
        MarkItDownOptions options;
        try
        {
            options = CreateOptions();
        }
        catch (InvalidOperationException ex)
        {
            output.WriteLine($"Skipping structured scenario: {ex.Message}");
            return;
        }

        var pdfAsset = "TestAssetCatalog.CLICSTMAN001V5SpecimenSubmissionManualPdf";
        var pdfCandidate = Path.Combine(TestAssetLoader.AssetsDirectory, pdfAsset);
        if (!File.Exists(pdfCandidate))
        {
            output.WriteLine("Skipping structured scenario because the PDF asset is missing.");
            return;
        }

        var client = new MarkItDownClient(options);
        var pdfPath = TestAssetLoader.GetAssetPath(pdfAsset);

        var structuredRequest = ConversionRequest.FromConfiguration(builder =>
        {
            builder.UsePdfConversionMode(PdfConversionMode.Structured);
            builder.ConfigureSegments(segments => segments with
            {
                Image = segments.Image with { EnableImageUnderstandingProvider = true }
            });
        });

        var structuredResult = await client.ConvertAsync(pdfPath, (IProgress<ConversionProgress>?)null, structuredRequest);
        await PersistResultAsync(pdfAsset, structuredResult, "structured-with-images");

        Assert.NotEmpty(structuredResult.Segments);
        Assert.Contains("Specimen Submission Manual", structuredResult.Markdown, StringComparison.OrdinalIgnoreCase);
        ValidateMetaMarkdown(structuredResult);

        var renderedRequest = ConversionRequest.FromConfiguration(builder =>
        {
            builder.UsePdfConversionMode(PdfConversionMode.RenderedPageOcr);
            builder.ConfigureSegments(segments => segments with
            {
                Image = segments.Image with { EnableImageUnderstandingProvider = true }
            });
        });

        var renderedResult = await client.ConvertAsync(pdfPath, (IProgress<ConversionProgress>?)null, renderedRequest);
        await PersistResultAsync(pdfAsset, renderedResult, "structured-ocr-compare");

        Assert.NotEmpty(renderedResult.Segments);
        Assert.Contains("Specimen Submission Manual", renderedResult.Markdown, StringComparison.OrdinalIgnoreCase);
        ValidateMetaMarkdown(renderedResult);

        Assert.NotEmpty(renderedResult.Artifacts.Images);
        var renderedOcrHits = renderedResult.Artifacts.Images.Where(image =>
            !string.IsNullOrWhiteSpace(image.RawText) ||
            (image.Metadata.TryGetValue(MetadataKeys.OcrText, out var text) && !string.IsNullOrWhiteSpace(text))).ToList();
        Assert.True(renderedOcrHits.Count > 0, "Expected rendered OCR conversion to populate OCR text for at least one page snapshot.");

        var structuredNormalized = NormalizeMarkdownForComparison(structuredResult.Markdown);
        var renderedNormalized = NormalizeMarkdownForComparison(renderedResult.Markdown);

        var structuredWords = ExtractDistinctWords(structuredResult.Markdown);
        var renderedWords = ExtractDistinctWords(renderedResult.Markdown);
        var sharedWordCount = structuredWords.Intersect(renderedWords, StringComparer.OrdinalIgnoreCase).Count();
        var smallestVocabulary = Math.Max(1, Math.Min(structuredWords.Count, renderedWords.Count));
        var wordOverlapRatio = sharedWordCount / (double)smallestVocabulary;

        output.WriteLine($"Structured words: {structuredWords.Count}");
        output.WriteLine($"Rendered words: {renderedWords.Count}");
        output.WriteLine($"Shared words: {sharedWordCount}");
        output.WriteLine($"Overlap ratio: {wordOverlapRatio:P2}");

        Assert.True(sharedWordCount >= 200, "Expected substantial word overlap between structured and rendered OCR outputs.");
        Assert.True(wordOverlapRatio >= 0.7, $"Structured vs rendered OCR word overlap ratio was too low ({wordOverlapRatio:P2}).");
    }

    private static MarkItDownOptions CreateOptions()
    {
        MarkItDownOptions options = new();

        var settings = AzureIntegrationConfigurationFactory.Load();

        AzureIntelligenceOptions? azure = null;
        if (settings.Document is not null || settings.Vision is not null || settings.Media is not null)
        {
            azure = new AzureIntelligenceOptions
            {
                DocumentIntelligence = settings.Document?.Options,
                Vision = settings.Vision?.Options,
                Media = settings.Media?.Options
            };
        }

        var aiProvider = AzureIntegrationConfigurationFactory.CreateAiModelProvider(settings.LanguageModels);
        if (aiProvider is null)
        {
            throw new InvalidOperationException(
                "Azure OpenAI chat client is not configured. Update azure-intelligence-config.json (see AzureIntegrationConfigDefaults) with valid credentials before running this harness.");
        }

        options = options with
        {
            AzureIntelligence = azure,
            AiModels = aiProvider,
            EnableAiImageEnrichment = true
        };

        return options;
    }

    private static void AssertSegmentsSequential(IReadOnlyList<DocumentSegment> segments)
    {
        var expected = 1;
        foreach (var segment in segments)
        {
            Assert.NotNull(segment);
            if (!segment.Number.HasValue)
            {
                throw new XunitException($"Segment \"{segment?.Label}\" is missing sequence number.");
            }

            Assert.Equal(expected, segment.Number.Value);
            Assert.Equal($"Page {expected}", segment.Label);
            Assert.True(segment.AdditionalMetadata.ContainsKey(MetadataKeys.Page), $"Segment {expected} is missing page metadata.");
            expected++;
        }
    }

    private static void AssertImagesPersisted(IList<ImageArtifact> images, int segmentCount)
    {
        foreach (var image in images)
        {
            var path = image.FilePath;
            if (path is null)
            {
                throw new XunitException("Image artifact did not persist to disk.");
            }

            Assert.False(string.IsNullOrWhiteSpace(path), "Image artifact path is blank.");
            Assert.True(File.Exists(path), $"Persisted image not found at {path}.");

            Assert.True(image.Metadata.TryGetValue(MetadataKeys.ArtifactPath, out var storedPath), "Image metadata is missing artifact path.");
            Assert.Equal(path, storedPath);

            if (image.SegmentIndex.HasValue)
            {
                Assert.InRange(image.SegmentIndex.Value, 0, segmentCount - 1);
            }
        }
    }

    private static async Task<string> PersistResultAsync(string assetName, DocumentConverterResult result, string? scenarioSuffix)
    {
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "ManualOutputs");
        Directory.CreateDirectory(outputDirectory);

        var sanitizedName = SanitizeFileName(Path.GetFileNameWithoutExtension(assetName));
        var scenarioPart = string.IsNullOrWhiteSpace(scenarioSuffix) ? string.Empty : $".{SanitizeFileName(scenarioSuffix)}";
        var baseName = string.IsNullOrWhiteSpace(sanitizedName) ? "manual-output" : sanitizedName;
        var fileName = $"{baseName}{scenarioPart}.metamd.md";

        var outputPath = Path.Combine(outputDirectory, fileName);
        await File.WriteAllTextAsync(outputPath, result.Markdown);
        return outputPath;
    }

    private static string? ResolveDescription(ImageArtifact image)
    {
        if (!string.IsNullOrWhiteSpace(image.DetailedDescription))
        {
            return image.DetailedDescription;
        }

        if (image.Metadata.TryGetValue(MetadataKeys.DetailedDescription, out var detailed) && !string.IsNullOrWhiteSpace(detailed))
        {
            return detailed;
        }

        if (image.Metadata.TryGetValue(MetadataKeys.Caption, out var caption) && !string.IsNullOrWhiteSpace(caption))
        {
            return caption;
        }

        if (!image.PageNumber.HasValue &&
            image.Metadata.TryGetValue(MetadataKeys.Page, out var pageValue) &&
            int.TryParse(pageValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPage))
        {
            return $"Image located on page {parsedPage}.";
        }

        if (image.PageNumber.HasValue)
        {
            return $"Image located on page {image.PageNumber.Value}.";
        }

        return "Image artifact captured without additional enrichment.";
    }

    private static void AssertDescriptionInMarkdown(ImageArtifact image, string? description, string markdown)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        var snippet = ExtractImageSnippet(image, markdown);
        Assert.False(string.Equals(snippet, "<not found>", StringComparison.Ordinal), "Image placeholder not found in markdown.");
        Assert.DoesNotContain("AI enrichment not available", snippet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Image captured without AI enrichment", snippet, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractImageSnippet(ImageArtifact image, string markdown)
    {
        var start = FindImageStart(markdown, image.PlaceholderMarkdown);
        if (start < 0)
        {
            start = FindImageUsingMetadata(markdown, image);
        }

        if (start < 0)
        {
            return "<not found>";
        }

        var end = FindImageBoundary(markdown, start);
        var length = Math.Min(end - start, 400);
        return markdown.Substring(start, length).Trim();
    }

    private static int FindImageStart(string markdown, string? placeholder)
    {
        if (string.IsNullOrWhiteSpace(placeholder))
        {
            return -1;
        }

        return markdown.IndexOf(placeholder, StringComparison.Ordinal);
    }

    private static int FindImageUsingMetadata(string markdown, ImageArtifact image)
    {
        var candidates = new[]
        {
            TryGetMetadata(image, MetadataKeys.ArtifactRelativePath),
            TryGetMetadata(image, MetadataKeys.ArtifactFileName),
            TryGetMetadata(image, MetadataKeys.ArtifactPath)
        };

        foreach (var candidate in candidates)
        {
            var start = FindByPath(markdown, candidate);
            if (start >= 0)
            {
                return start;
            }
        }

        return -1;
    }

    private static string? TryGetMetadata(ImageArtifact image, string key)
        => image.Metadata.TryGetValue(key, out var value) ? value : null;

    private static int FindByPath(string markdown, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return -1;
        }

        static int Locate(string markdown, string candidate)
        {
            var index = markdown.IndexOf(candidate, StringComparison.Ordinal);
            if (index < 0)
            {
                return -1;
            }

            var start = markdown.LastIndexOf("![", index, StringComparison.Ordinal);
            return start;
        }

        var direct = Locate(markdown, $"({path})");
        if (direct >= 0)
        {
            return direct;
        }

        var encoded = EscapePath(path);
        if (!string.Equals(encoded, path, StringComparison.Ordinal))
        {
            var encodedResult = Locate(markdown, $"({encoded})");
            if (encodedResult >= 0)
            {
                return encodedResult;
            }
        }

        return -1;
    }

    private static int FindImageBoundary(string markdown, int start)
    {
        var nextImage = markdown.IndexOf("\n![", start + 2, StringComparison.Ordinal);
        if (nextImage < 0)
        {
            nextImage = markdown.IndexOf("\n**Image:**", start + 2, StringComparison.Ordinal);
        }

        if (nextImage < 0)
        {
            nextImage = markdown.Length;
        }

        return nextImage;
    }

    private static string EscapePath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.None);
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = Uri.EscapeDataString(segments[i]);
        }

        return string.Join("/", segments);
    }

    private static void ValidateMetaMarkdown(DocumentConverterResult result)
    {
        var markdown = result.Markdown ?? string.Empty;
        var trimmed = markdown.TrimStart();
        Assert.StartsWith("---", trimmed);

        if (result.Artifacts.Images.Count > 0)
        {
            foreach (var image in result.Artifacts.Images)
            {
                var description = ResolveDescription(image);
                AssertDescriptionInMarkdown(image, description, markdown);
            }
        }
    }

    private static int CountOccurrences(string content, string value)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static IReadOnlyList<string> NormalizeMarkdownForComparison(string markdown)
    {
        var body = ExtractBody(markdown).Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = body.Split('\n', StringSplitOptions.TrimEntries);
        var normalized = new List<string>(lines.Length);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("---", StringComparison.Ordinal) && line.EndsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("<!--", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("![", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("source:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("fileName:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("generated:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("mimeType:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("pages:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("images:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("tables:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var collapsed = CollapseWhitespace(line);
            if (collapsed.Length > 0)
            {
                normalized.Add(collapsed);
            }
        }

        return normalized;
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;

        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(ch);
            lastWasSpace = false;
        }

        return builder.ToString().Trim();
    }

    private static HashSet<string> ExtractDistinctWords(string text)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return words;
        }

        var buffer = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                AddWordIfNeeded(words, buffer);
            }
        }

        AddWordIfNeeded(words, buffer);
        return words;
    }

    private static void AddWordIfNeeded(HashSet<string> words, StringBuilder buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        if (buffer.Length >= 3)
        {
            words.Add(buffer.ToString());
        }

        buffer.Clear();
    }

    private sealed record ConversionScenario(
        string Description,
        string? NameSuffix,
        Action<ConversionRequestBuilder>? Configure);

    private static string SanitizeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", value.Split(invalid, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string ExtractBody(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        const string delimiter = "\n---";
        if (!markdown.StartsWith("---", StringComparison.Ordinal))
        {
            return markdown.Trim();
        }

        var secondDelimiterIndex = markdown.IndexOf(delimiter, 3, StringComparison.Ordinal);
        if (secondDelimiterIndex < 0)
        {
            return markdown.Trim();
        }

        var bodyStart = secondDelimiterIndex + delimiter.Length;
        if (bodyStart < markdown.Length && markdown[bodyStart] == '\n')
        {
            bodyStart++;
        }

        return markdown[bodyStart..].Trim();
    }
}
