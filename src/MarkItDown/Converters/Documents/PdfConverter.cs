using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using PDFtoImage;
using static PDFtoImage.Conversion;
using SkiaSharp;
using UglyToad.PdfPig;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for PDF files to Markdown using PdfPig for text extraction and optional page image rendering.
/// </summary>
public sealed class PdfConverter : DocumentPipelineConverterBase
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    private static readonly HashSet<string> AcceptedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        MimeHelper.PDF
    };

    private readonly IPdfTextExtractor textExtractor;
    private readonly IPdfImageRenderer imageRenderer;
    private readonly SegmentOptions segmentOptions;
    private readonly IDocumentIntelligenceProvider? documentIntelligenceProvider;
    private readonly IImageUnderstandingProvider? imageUnderstandingProvider;
    private readonly IConversionPipeline conversionPipeline;

    public PdfConverter(
        SegmentOptions? segmentOptions = null,
        IDocumentIntelligenceProvider? documentProvider = null,
        IImageUnderstandingProvider? imageProvider = null,
        IConversionPipeline? pipeline = null)
        : this(new PdfPigTextExtractor(), new PdfToImageRenderer(), segmentOptions, documentProvider, imageProvider, pipeline)
    {
    }

    private async Task<PdfExtractionResult> ExtractAsync(
        string pdfPath,
        StreamInfo streamInfo,
        ImageArtifactPersistor imagePersistor,
        SegmentOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.Pdf.TreatPagesAsImages)
        {
            var analysisExtraction = await TryBuildExtractionFromDocumentIntelligenceAsync(pdfPath, streamInfo, imagePersistor, options, cancellationToken).ConfigureAwait(false);
            if (analysisExtraction is not null)
            {
                return analysisExtraction;
            }
        }

        if (options.Pdf.TreatPagesAsImages)
        {
            return await BuildExtractionFromPageImagesAsync(pdfPath, streamInfo, imagePersistor, options, cancellationToken).ConfigureAwait(false);
        }

        return await BuildExtractionFromPdfPigAsync(pdfPath, streamInfo, imagePersistor, options, cancellationToken).ConfigureAwait(false);
    }

    private SegmentOptions ResolveSegmentOptions()
        => ConversionContextAccessor.Current?.Segments ?? segmentOptions;

    private ArtifactStorageOptions ResolveStorageOptions()
        => ConversionContextAccessor.Current?.Storage ?? ArtifactStorageOptions.Default;

    internal PdfConverter(
        IPdfTextExtractor textExtractor,
        IPdfImageRenderer imageRenderer,
        SegmentOptions? segmentOptions = null,
        IDocumentIntelligenceProvider? documentProvider = null,
        IImageUnderstandingProvider? imageProvider = null,
        IConversionPipeline? pipeline = null)
        : base(priority: 200)
    {
        this.textExtractor = textExtractor ?? throw new ArgumentNullException(nameof(textExtractor));
        this.imageRenderer = imageRenderer ?? throw new ArgumentNullException(nameof(imageRenderer));
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
        documentIntelligenceProvider = documentProvider;
        imageUnderstandingProvider = imageProvider;
        conversionPipeline = pipeline ?? ConversionPipeline.Empty;
    }

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = streamInfo.ResolveMimeType();
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && AcceptedExtensions.Contains(extension))
        {
            return true;
        }

        if (normalizedMime is not null && AcceptedMimeTypes.Contains(normalizedMime))
        {
            return true;
        }

        return streamInfo.MimeType is not null && AcceptedMimeTypes.Contains(streamInfo.MimeType);
    }

    public override bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (!AcceptsInput(streamInfo))
            return false;

        if (stream.CanSeek && stream.Length > 4)
        {
            var originalPosition = stream.Position;
            try
            {
                stream.Position = 0;
                Span<byte> buffer = stackalloc byte[4];
                var bytesRead = stream.Read(buffer);
                stream.Position = originalPosition;

                if (bytesRead == 4)
                {
                    return buffer[0] == '%' && buffer[1] == 'P' && buffer[2] == 'D' && buffer[3] == 'F';
                }
            }
            catch
            {
                stream.Position = originalPosition;
            }
        }

        return true;
    }

    private sealed class PdfExtractionResult
    {
        public PdfExtractionResult(List<DocumentSegment> segments, ConversionArtifacts artifacts, string rawText)
        {
            Segments = segments;
            Artifacts = artifacts;
            RawText = rawText;
        }

        public List<DocumentSegment> Segments { get; }

        public ConversionArtifacts Artifacts { get; }

        public string RawText { get; }
    }

    public override Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        => ConvertAsync(stream, streamInfo, modeOverride: null, cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(
        Stream stream,
        StreamInfo streamInfo,
        PdfConversionMode mode,
        CancellationToken cancellationToken = default)
        => ConvertAsync(stream, streamInfo, modeOverride: mode, cancellationToken);

    private async Task<DocumentConverterResult> ConvertAsync(
        Stream stream,
        StreamInfo streamInfo,
        PdfConversionMode? modeOverride,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(streamInfo);

        ArtifactWorkspace? artifactWorkspace = null;
        ArtifactStorageOptions storageOptions = ResolveStorageOptions();
        string? storedSourcePath = null;
        string? storedMarkdownPath = null;
        await using var source = await MaterializeSourceAsync(stream, streamInfo, ".pdf", cancellationToken).ConfigureAwait(false);

        try
        {
            var baselineOptions = ResolveSegmentOptions();
            var effectiveOptions = modeOverride.HasValue
                ? ApplyModeOverride(baselineOptions, modeOverride.Value)
                : baselineOptions;

            artifactWorkspace = ArtifactWorkspaceFactory.CreateWorkspace(streamInfo, effectiveOptions, storageOptions);

            if (storageOptions.CopySourceDocument)
            {
                var sourceExtension = Path.GetExtension(streamInfo.FileName);
                if (string.IsNullOrWhiteSpace(sourceExtension))
                {
                    sourceExtension = Path.GetExtension(source.FilePath);
                }

                if (string.IsNullOrWhiteSpace(sourceExtension))
                {
                    sourceExtension = ".pdf";
                }

                var sourceFileName = FileNameSanitizer.BuildFileName(streamInfo.FileName, "source", sourceExtension);
                var sourceMime = streamInfo.ResolveMimeType() ?? MimeHelper.PDF;
                storedSourcePath = artifactWorkspace.PersistFile(sourceFileName, source.FilePath, sourceMime, cancellationToken);
            }

            var imagePersistor = new ImageArtifactPersistor(artifactWorkspace, streamInfo);

            var extraction = await ExtractAsync(
                source.FilePath,
                streamInfo,
                imagePersistor,
                effectiveOptions,
                cancellationToken).ConfigureAwait(false);

            await conversionPipeline.ExecuteAsync(streamInfo, extraction.Artifacts, extraction.Segments, cancellationToken).ConfigureAwait(false);

            var titleHint = ExtractTitle(extraction.RawText);
            var generatedAt = DateTime.UtcNow;
            var meta = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, streamInfo, effectiveOptions, titleHint, generatedAt);
            var title = meta.Title
                ?? titleHint
                ?? Path.GetFileNameWithoutExtension(streamInfo.FileName ?? streamInfo.LocalPath ?? streamInfo.Url)
                ?? "Document";

            var pageCount = extraction.Segments.Count(static segment => segment.Type == SegmentType.Page);
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataKeys.DocumentPages] = pageCount.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentImages] = extraction.Artifacts.Images.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentTables] = extraction.Artifacts.Tables.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.WorkspaceDirectory] = artifactWorkspace.DirectoryPath
            };

            if (!string.IsNullOrWhiteSpace(titleHint))
            {
                metadata[MetadataKeys.DocumentTitleHint] = titleHint!;
            }

            metadata[MetadataKeys.DocumentTitle] = title;

            if (!string.IsNullOrWhiteSpace(storedSourcePath))
            {
                metadata[MetadataKeys.WorkspaceSourceFile] = storedSourcePath;
            }

            if (storageOptions.PersistMarkdown)
            {
                var markdownFileName = FileNameSanitizer.BuildFileName(streamInfo.FileName, "document", ".md");
                storedMarkdownPath = artifactWorkspace.PersistText(markdownFileName, meta.Markdown, MimeHelper.MARKDOWN, cancellationToken);
                metadata[MetadataKeys.WorkspaceMarkdownFile] = storedMarkdownPath;
            }

            ReportPdfProgress(
                "pdf-completed",
                Math.Max(pageCount, 0),
                Math.Max(pageCount, 1),
                $"pages={pageCount} images={extraction.Artifacts.Images.Count} tables={extraction.Artifacts.Tables.Count}");

            string MarkdownFactory()
            {
                var recomposed = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, streamInfo, effectiveOptions, titleHint, generatedAt);
                return recomposed.Markdown;
            }

            return DocumentConverterResult.FromFactory(
                MarkdownFactory,
                title,
                extraction.Segments,
                extraction.Artifacts,
                metadata,
                artifactDirectory: artifactWorkspace.DirectoryPath,
                cleanup: null,
                asyncCleanup: artifactWorkspace,
                generatedAtUtc: generatedAt);
        }
        catch (Exception ex) when (ex is not MarkItDownException)
        {
            if (artifactWorkspace is not null)
            {
                await artifactWorkspace.DisposeAsync().ConfigureAwait(false);
            }

            throw new FileConversionException($"Failed to convert PDF file: {ex.Message}", ex);
        }
        catch
        {
            if (artifactWorkspace is not null)
            {
                await artifactWorkspace.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private static SegmentOptions ApplyModeOverride(SegmentOptions baseline, PdfConversionMode mode)
    {
        var treatPagesAsImages = mode == PdfConversionMode.RenderedPageOcr;
        var pdfOptions = baseline.Pdf with { TreatPagesAsImages = treatPagesAsImages };
        return baseline with { Pdf = pdfOptions };
    }

    private async Task<PdfExtractionResult?> TryBuildExtractionFromDocumentIntelligenceAsync(
        string pdfPath,
        StreamInfo streamInfo,
        ImageArtifactPersistor imagePersistor,
        SegmentOptions options,
        CancellationToken cancellationToken)
    {
        var context = ConversionContextAccessor.Current;
        var provider = context?.Providers.Document ?? documentIntelligenceProvider;
        var request = context?.Request.Intelligence.Document;

        if (provider is null || !options.Image.EnableDocumentIntelligence)
        {
            return null;
        }

        try
        {
            await using var providerStream = OpenReadOnlyFile(pdfPath);
            var analysis = await provider.AnalyzeAsync(providerStream, streamInfo, request, cancellationToken).ConfigureAwait(false);
            if (analysis is null || analysis.Pages.Count == 0)
            {
                return null;
            }

            return await BuildExtractionFromDocumentIntelligenceAsync(analysis, pdfPath, streamInfo, imagePersistor, options, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task<PdfExtractionResult> BuildExtractionFromDocumentIntelligenceAsync(
        DocumentIntelligenceResult analysis,
        string pdfPath,
        StreamInfo streamInfo,
        ImageArtifactPersistor imagePersistor,
        SegmentOptions options,
        CancellationToken cancellationToken)
    {
        var artifacts = new ConversionArtifacts();
        var pageMap = new SortedDictionary<int, PageAccumulator>();
        var processedTables = new HashSet<int>();
        var pagesWithInlineImages = new HashSet<int>();

        var orderedPages = analysis.Pages
            .OrderBy(p => p.PageNumber)
            .ToList();
        var distinctPageNumbers = orderedPages
            .Select(p => p.PageNumber)
            .Distinct()
            .ToList();

        var tableCount = 0;

        foreach (var page in orderedPages)
        {
            var accumulator = GetOrCreatePageAccumulator(pageMap, page.PageNumber);

            if (!string.IsNullOrWhiteSpace(page.Text))
            {
                AppendMarkdown(accumulator.Markdown, page.Text.Trim());
            }

            foreach (var tableIndex in page.TableIndices.Distinct())
            {
                if (tableIndex < 0 || tableIndex >= analysis.Tables.Count)
                {
                    continue;
                }

                var table = analysis.Tables[tableIndex];
                var normalizedRows = NormalizeDocumentTableRows(table);
                var tableMarkdown = BuildMarkdownFromRows(normalizedRows);
                if (string.IsNullOrWhiteSpace(tableMarkdown))
                {
                    continue;
                }

                string? tableComment = null;
                if (table.Metadata.TryGetValue(MetadataKeys.TableComment, out var storedComment) && !string.IsNullOrWhiteSpace(storedComment))
                {
                    tableComment = storedComment;
                }
                else if (table.Metadata.TryGetValue(MetadataKeys.TablePageRange, out var pageRange) && !string.IsNullOrWhiteSpace(pageRange))
                {
                    tableComment = $"<!-- Table spans pages {pageRange} -->";
                }

                var replacementMarkdown = tableComment is null
                    ? tableMarkdown
                    : $"{tableComment}{Environment.NewLine}{tableMarkdown}";

                var placeholder = $"{{{{TABLE:{tableIndex}}}}}";
                var replaced = TryReplacePlaceholder(accumulator.Markdown, placeholder, replacementMarkdown);

                if (!replaced)
                {
                    AppendMarkdown(accumulator.Markdown, replacementMarkdown);
                }

                if (processedTables.Add(tableIndex))
                {
                    tableCount++;
                    var rows = normalizedRows.Select(static row => (IList<string>)row.ToList()).ToList();
                    var tableArtifact = new TableArtifact(rows, page.PageNumber, streamInfo.FileName, $"Table {tableCount}");
                    tableArtifact.Metadata[MetadataKeys.TableIndex] = tableCount.ToString(CultureInfo.InvariantCulture);
                    tableArtifact.Metadata[MetadataKeys.TablePageStart] = page.PageNumber.ToString(CultureInfo.InvariantCulture);
                    tableArtifact.Metadata[MetadataKeys.TablePageRange] = page.PageNumber.ToString(CultureInfo.InvariantCulture);
                    foreach (var pair in table.Metadata)
                    {
                        tableArtifact.Metadata[pair.Key] = pair.Value;
                    }

                    artifacts.Tables.Add(tableArtifact);
                }
            }

            foreach (var image in analysis.Images.Where(img => img.PageNumber == page.PageNumber))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var artifact = await CreateImageArtifactAsync(image, streamInfo, options, cancellationToken).ConfigureAwait(false);
                imagePersistor.Persist(artifact, cancellationToken);

                var caption = artifact.Metadata.TryGetValue(MetadataKeys.Caption, out var storedCaption) ? storedCaption : artifact.Label;
                var placeholder = ImagePlaceholderFormatter.BuildPlaceholder(artifact, caption, $"Image (page {image.PageNumber})");
                artifact.PlaceholderMarkdown = placeholder;
                AppendMarkdown(accumulator.Markdown, placeholder);
                accumulator.Images.Add(artifact);
                pagesWithInlineImages.Add(page.PageNumber);
            }
        }

        await AppendMissingPageSnapshotsAsync(
            distinctPageNumbers,
            pagesWithInlineImages,
            pdfPath,
            streamInfo,
            pageMap,
            artifacts,
            imagePersistor,
            options,
            cancellationToken).ConfigureAwait(false);

        var segments = new List<DocumentSegment>();
        var totalPages = pageMap.Count;
        var processedPages = 0;
        var totalImages = pageMap.Values.Sum(acc => acc.Images.Count);
        var assignedImages = 0;

        foreach (var (pageNumber, accumulator) in pageMap)
        {
            var markdown = accumulator.Markdown.ToString().TrimEnd();
            if (markdown.Length == 0)
            {
                markdown = $"<!-- Page {pageNumber} contained layout-only content -->";
            }

            var metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Page] = pageNumber.ToString(CultureInfo.InvariantCulture)
            };

            var segment = new DocumentSegment(
                markdown: markdown,
                type: SegmentType.Page,
                number: pageNumber,
                label: $"Page {pageNumber}",
                source: streamInfo.FileName,
                additionalMetadata: metadata);

            var index = segments.Count;
            segments.Add(segment);
            artifacts.TextBlocks.Add(new TextArtifact(markdown, pageNumber, streamInfo.FileName, segment.Label));

            processedPages++;
            ReportPdfProgress("pdf-page", processedPages, Math.Max(totalPages, 1), $"page={pageNumber}");

            foreach (var artifact in accumulator.Images)
            {
                artifact.SegmentIndex = index;
                artifact.Metadata[MetadataKeys.Page] = pageNumber.ToString(CultureInfo.InvariantCulture);
                artifact.Label ??= $"Image {artifacts.Images.Count + 1}";
                artifacts.Images.Add(artifact);
                assignedImages++;
                if (totalImages > 0)
                {
                    ReportPdfProgress("pdf-images", assignedImages, totalImages, $"page={pageNumber}");
                }
            }
        }

        artifacts.Metadata[MetadataKeys.DocumentIntelligenceProvider] = MetadataValues.ProviderAzureDocumentIntelligence;

        var rawText = ComposeRawText(segments);

        ReportPdfProgress("pdf-parser", 1, 1, $"strategy=document-intelligence pages={segments.Count}");

        return new PdfExtractionResult(segments, artifacts, rawText);
    }

    private PdfExtractionResult BuildExtractionFromExtractedText(IReadOnlyList<PdfPageText> pages, IReadOnlyList<string> pageImages, StreamInfo streamInfo, ImageArtifactPersistor imagePersistor, CancellationToken cancellationToken)
    {
        var segments = new List<DocumentSegment>();
        var artifacts = new ConversionArtifacts();
        var rawTextBuilder = new StringBuilder();
        var totalPages = pages.Count;
        var processedPages = 0;

        foreach (var page in pages)
        {
            var markdown = ConvertTextToMarkdown(page.Text);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                continue;
            }

            var metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Page] = page.PageNumber.ToString(CultureInfo.InvariantCulture)
            };

            var segment = new DocumentSegment(
                markdown: markdown,
                type: SegmentType.Page,
                number: page.PageNumber,
                label: $"Page {page.PageNumber}",
                source: streamInfo.FileName,
                additionalMetadata: metadata);

            segments.Add(segment);
            artifacts.TextBlocks.Add(new TextArtifact(markdown, page.PageNumber, streamInfo.FileName, segment.Label));

            if (rawTextBuilder.Length > 0)
            {
                rawTextBuilder.AppendLine();
            }

            rawTextBuilder.AppendLine(markdown);

            processedPages++;
            ReportPdfProgress("pdf-page", processedPages, Math.Max(totalPages, 1), $"page={page.PageNumber}");
        }

        if (pageImages.Count > 0)
        {
            segments.Add(new DocumentSegment(
                markdown: "## Page Images",
                type: SegmentType.Section,
                label: "Page Images",
                source: streamInfo.FileName));

            for (var i = 0; i < pageImages.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var metadata = new Dictionary<string, string>
                {
                    [MetadataKeys.Page] = (i + 1).ToString(CultureInfo.InvariantCulture)
                };

                var imageBytes = Convert.FromBase64String(pageImages[i]);
                var artifact = new ImageArtifact(imageBytes, MimeHelper.PNG, i + 1, streamInfo.FileName, $"Page {i + 1} Image");
                artifact.Metadata[MetadataKeys.Page] = (i + 1).ToString(CultureInfo.InvariantCulture);

                imagePersistor.Persist(artifact, cancellationToken);

                var summary = $"PDF page {i + 1}";
                var context = $"Image (page {i + 1})";
                var placeholder = ImagePlaceholderFormatter.BuildPlaceholder(artifact, summary, context);
                artifact.PlaceholderMarkdown = placeholder;

                var segmentIndex = segments.Count;
                segments.Add(new DocumentSegment(
                    markdown: placeholder,
                    type: SegmentType.Image,
                    number: i + 1,
                    label: $"Page {i + 1} Image",
                    source: streamInfo.FileName,
                    additionalMetadata: metadata));

                artifact.SegmentIndex = segmentIndex;
                artifacts.Images.Add(artifact);

                ReportPdfProgress("pdf-images", i + 1, Math.Max(pageImages.Count, 1), $"page={i + 1}");
            }
        }

        return new PdfExtractionResult(segments, artifacts, rawTextBuilder.ToString().Trim());
    }

    private async Task<PdfExtractionResult> BuildExtractionFromPdfPigAsync(
        string pdfPath,
        StreamInfo streamInfo,
        ImageArtifactPersistor imagePersistor,
        SegmentOptions options,
        CancellationToken cancellationToken)
    {
        var pages = await textExtractor.ExtractTextAsync(pdfPath, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<string> pageImages;

        try
        {
            pageImages = await imageRenderer.RenderImagesAsync(pdfPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            pageImages = Array.Empty<string>();
        }

        var extraction = BuildExtractionFromExtractedText(pages, pageImages, streamInfo, imagePersistor, cancellationToken);
        ReportPdfProgress("pdf-parser", 1, 1, $"strategy=pdfpig pages={pages.Count}");
        return extraction;
    }

    private async Task<PdfExtractionResult> BuildExtractionFromPageImagesAsync(
        string pdfPath,
        StreamInfo streamInfo,
        ImageArtifactPersistor imagePersistor,
        SegmentOptions options,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> renderedPages;

        try
        {
            renderedPages = await imageRenderer.RenderImagesAsync(pdfPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            renderedPages = Array.Empty<string>();
        }

        if (renderedPages.Count == 0)
        {
            return await BuildExtractionFromPdfPigAsync(pdfPath, streamInfo, imagePersistor, options, cancellationToken).ConfigureAwait(false);
        }

        var segments = new List<DocumentSegment>();
        var artifacts = new ConversionArtifacts();
        var rawTextBuilder = new StringBuilder();
        var totalRendered = renderedPages.Count;
        var processedPages = 0;

        for (var i = 0; i < renderedPages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var base64 = renderedPages[i];
            if (string.IsNullOrWhiteSpace(base64))
            {
                continue;
            }

            byte[] imageBytes;
            try
            {
                imageBytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                continue;
            }

            var pageNumber = i + 1;
            var metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Page] = pageNumber.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.Snapshot] = bool.TrueString.ToLowerInvariant()
            };

            var documentImage = new DocumentImageResult(
                pageNumber,
                imageBytes,
                MimeHelper.PNG,
                caption: $"Page {pageNumber} Snapshot",
                metadata: metadata);

            var artifact = await CreateImageArtifactAsync(documentImage, streamInfo, options, cancellationToken).ConfigureAwait(false);
            artifact.Metadata[MetadataKeys.Snapshot] = bool.TrueString.ToLowerInvariant();
            artifact.Metadata[MetadataKeys.Page] = pageNumber.ToString(CultureInfo.InvariantCulture);

            imagePersistor.Persist(artifact, cancellationToken);

            var summary = artifact.Label ?? $"PDF page {pageNumber}";
            var placeholder = ImagePlaceholderFormatter.BuildPlaceholder(artifact, summary, $"PDF page {pageNumber}");
            artifact.PlaceholderMarkdown = placeholder;

            string pageText = string.Empty;
            if (!string.IsNullOrWhiteSpace(artifact.RawText))
            {
                pageText = ConvertTextToMarkdown(artifact.RawText!);
            }

            var pageBuilder = new StringBuilder();
            pageBuilder.AppendLine(placeholder);

            if (!string.IsNullOrWhiteSpace(pageText))
            {
                pageBuilder.AppendLine();
                pageBuilder.AppendLine(pageText);
            }

            var pageMarkdown = pageBuilder.ToString().TrimEnd();

            var segmentMetadata = new Dictionary<string, string>
            {
                [MetadataKeys.Page] = pageNumber.ToString(CultureInfo.InvariantCulture)
            };

            var segment = new DocumentSegment(
                markdown: pageMarkdown,
                type: SegmentType.Page,
                number: pageNumber,
                label: $"Page {pageNumber}",
                source: streamInfo.FileName,
                additionalMetadata: segmentMetadata);

            var segmentIndex = segments.Count;
            segments.Add(segment);
            artifact.SegmentIndex = segmentIndex;
            artifacts.Images.Add(artifact);

            if (!string.IsNullOrWhiteSpace(pageText))
            {
                artifacts.TextBlocks.Add(new TextArtifact(pageText, pageNumber, streamInfo.FileName, segment.Label));

                if (rawTextBuilder.Length > 0)
                {
                    rawTextBuilder.AppendLine();
                }

                rawTextBuilder.AppendLine(pageText);
            }

            processedPages++;
            ReportPdfProgress("pdf-page", processedPages, Math.Max(totalRendered, 1), $"page={pageNumber}");
        }

        ReportPdfProgress("pdf-parser", 1, 1, $"strategy=page-images pages={segments.Count}");

        return new PdfExtractionResult(segments, artifacts, rawTextBuilder.ToString().Trim());
    }

    private async Task AppendMissingPageSnapshotsAsync(
        IReadOnlyList<int> pageNumbers,
        HashSet<int> pagesWithInlineImages,
        string pdfPath,
        StreamInfo streamInfo,
        SortedDictionary<int, PageAccumulator> pageMap,
        ConversionArtifacts artifacts,
        ImageArtifactPersistor imagePersistor,
        SegmentOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.Pdf.TreatPagesAsImages)
        {
            return;
        }

        if (pageNumbers.Count == 0)
        {
            return;
        }

        var missingPages = pageNumbers
            .Where(page => !pagesWithInlineImages.Contains(page))
            .Distinct()
            .OrderBy(page => page)
            .ToList();

        if (missingPages.Count == 0)
        {
            return;
        }

        IReadOnlyList<string> renderedPages;

        try
        {
            renderedPages = await imageRenderer.RenderImagesAsync(pdfPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Rendering support is optional for document intelligence; ignore failures
            // so that conversions can still succeed when the renderer is unavailable.
            return;
        }

        if (renderedPages.Count == 0)
        {
            return;
        }

        foreach (var page in missingPages)
        {
            if (page <= 0 || page > renderedPages.Count)
            {
                continue;
            }

            var base64 = renderedPages[page - 1];
            if (string.IsNullOrWhiteSpace(base64))
            {
                continue;
            }

            byte[] imageBytes;
            try
            {
                imageBytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                continue;
            }

            var accumulator = GetOrCreatePageAccumulator(pageMap, page);

            var documentImage = new DocumentImageResult(
                page,
                imageBytes,
                MimeHelper.PNG,
                caption: $"Page {page} Snapshot",
                metadata: new Dictionary<string, string>
                {
                    [MetadataKeys.Page] = page.ToString(CultureInfo.InvariantCulture),
                    [MetadataKeys.Snapshot] = bool.TrueString.ToLowerInvariant()
                });

            var artifact = await CreateImageArtifactAsync(documentImage, streamInfo, options, cancellationToken).ConfigureAwait(false);
            artifact.Metadata[MetadataKeys.Snapshot] = bool.TrueString.ToLowerInvariant();
            artifact.Metadata[MetadataKeys.Page] = page.ToString(CultureInfo.InvariantCulture);

            imagePersistor.Persist(artifact, cancellationToken);

            var placeholder = artifact.PlaceholderMarkdown;
            if (string.IsNullOrWhiteSpace(placeholder))
            {
                var summary = artifact.Label ?? $"PDF page {page}";
                placeholder = ImagePlaceholderFormatter.BuildPlaceholder(artifact, summary, $"Image (page {page})");
                artifact.PlaceholderMarkdown = placeholder;
            }

            AppendMarkdown(accumulator.Markdown, placeholder!);
            accumulator.Images.Add(artifact);
        }
    }

    private async Task<ImageArtifact> CreateImageArtifactAsync(DocumentImageResult image, StreamInfo streamInfo, SegmentOptions options, CancellationToken cancellationToken)
    {
        var artifact = new ImageArtifact(image.Content, image.ContentType, image.PageNumber, streamInfo.FileName)
        {
            Label = image.Caption ?? $"Image on page {image.PageNumber}"
        };

        artifact.Metadata[MetadataKeys.Page] = image.PageNumber.ToString(CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(image.Caption))
        {
            artifact.Metadata[MetadataKeys.Caption] = image.Caption!;
        }

        foreach (var pair in image.Metadata)
        {
            artifact.Metadata[pair.Key] = pair.Value;
        }

        if (image.Metadata.TryGetValue(MetadataKeys.OcrText, out var ocr) && !string.IsNullOrWhiteSpace(ocr))
        {
            artifact.RawText = ocr;
        }

        if (imageUnderstandingProvider is not null && options.Image.EnableImageUnderstandingProvider)
        {
            try
            {
                var context = ConversionContextAccessor.Current;
                var provider = context?.Providers.Image ?? imageUnderstandingProvider;
                var request = context?.Request.Intelligence.Image;

                if (provider is null)
                {
                    return artifact;
                }

                if (!artifact.Metadata.ContainsKey(MetadataKeys.Provider))
                {
                    artifact.Metadata[MetadataKeys.Provider] = provider.GetType().Name;
                }

                await using var imageHandle = await DiskBufferHandle.FromBytesAsync(image.Content, InferImageExtension(image.ContentType), cancellationToken).ConfigureAwait(false);
                await using var analysisStream = imageHandle.OpenRead();
                var result = await provider!.AnalyzeAsync(analysisStream, streamInfo, request, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(result?.Caption))
                {
                    artifact.Metadata[MetadataKeys.Caption] = result!.Caption!;
                    artifact.Label = result.Caption;
                }

                if (!string.IsNullOrWhiteSpace(result?.Text))
                {
                    artifact.Metadata[MetadataKeys.OcrText] = result!.Text!;
                    artifact.RawText = result.Text;
                }
            }
            catch
            {
                // Ignore analysis failures; downstream middleware may handle enrichment.
            }
        }

        return artifact;
    }

    private static string? InferImageExtension(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return ".img";
        }

        if (MimeHelper.TryGetExtensions(contentType, out var extensions) && extensions.Count > 0)
        {
            var candidate = extensions.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.StartsWith(".", StringComparison.Ordinal) ? candidate : "." + candidate;
            }
        }

        return ".img";
    }

    private static void ReportPdfProgress(string stage, int completed, int total, string? details)
    {
        var context = ConversionContextAccessor.Current;
        var progress = context?.Progress;
        if (progress is null)
        {
            return;
        }

        var detailLevel = context?.ProgressDetail ?? ProgressDetailLevel.Basic;
        if (detailLevel == ProgressDetailLevel.Basic &&
            (string.Equals(stage, "pdf-page", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(stage, "pdf-images", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var normalizedTotal = Math.Max(total, 1);
        progress.Report(new ConversionProgress(stage, Math.Clamp(completed, 0, normalizedTotal), normalizedTotal, details));
    }

    private static PageAccumulator GetOrCreatePageAccumulator(SortedDictionary<int, PageAccumulator> pageMap, int pageNumber)
    {
        if (!pageMap.TryGetValue(pageNumber, out var accumulator))
        {
            accumulator = new PageAccumulator();
            pageMap[pageNumber] = accumulator;
        }

        return accumulator;
    }

    private static void AppendMarkdown(StringBuilder builder, string markdown)
    {
        var normalized = markdown.TrimEnd();
        if (normalized.Length == 0)
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.AppendLine(normalized);
    }

    private static bool TryReplacePlaceholder(StringBuilder builder, string placeholder, string replacement)
    {
        var text = builder.ToString();
        if (!text.Contains(placeholder, StringComparison.Ordinal))
        {
            return false;
        }

        var updated = text.Replace(placeholder, replacement, StringComparison.Ordinal);
        builder.Clear();
        builder.Append(updated);
        return true;
    }

    private static string ComposeRawText(IEnumerable<DocumentSegment> segments)
    {
        var builder = new StringBuilder();

        foreach (var segment in segments)
        {
            if (segment is null || string.IsNullOrWhiteSpace(segment.Markdown))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine(segment.Markdown.TrimEnd());
        }

        return builder.ToString().Trim();
    }

    private static string BuildMarkdownFromRows(IReadOnlyList<IList<string>> rows)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var header = rows[0];
        builder.Append("| ");
        foreach (var cell in header)
        {
            builder.Append(EscapeMarkdownTableCell(cell)).Append(" | ");
        }
        builder.AppendLine();

        builder.Append("| ");
        foreach (var _ in header)
        {
            builder.Append("--- | ");
        }
        builder.AppendLine();

        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            builder.Append("| ");
            foreach (var cell in rows[rowIndex])
            {
                builder.Append(EscapeMarkdownTableCell(cell)).Append(" | ");
            }
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static List<IList<string>> NormalizeDocumentTableRows(DocumentTableResult table)
    {
        if (table.Rows.Count == 0)
        {
            return new List<IList<string>>();
        }

        var rows = table.Rows
            .Select(static row => (IList<string>)row.Select(static cell => cell?.Trim() ?? string.Empty).ToList())
            .ToList();

        var maxColumns = rows.Max(static r => r.Count);
        foreach (var row in rows)
        {
            while (row.Count < maxColumns)
            {
                row.Add(string.Empty);
            }
        }

        PropagateTableValues(rows, startRow: 1);
        return rows;
    }

    private static void PropagateTableValues(IList<IList<string>> tableData, int startRow)
    {
        if (tableData.Count <= startRow)
        {
            return;
        }

        var columnCount = tableData[startRow].Count;
        for (var col = 0; col < columnCount; col++)
        {
            string? lastValue = null;

            for (var row = startRow; row < tableData.Count; row++)
            {
                var value = tableData[row][col];
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (!string.IsNullOrWhiteSpace(lastValue))
                    {
                        tableData[row][col] = lastValue!;
                    }
                }
                else
                {
                    lastValue = value;
                }
            }
        }
    }

    private sealed class PageAccumulator
    {
        public StringBuilder Markdown { get; } = new();

        public List<ImageArtifact> Images { get; } = new();
    }

    private static string EscapeMarkdownTableCell(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ").Trim();
    }

    private static string ConvertTextToMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var lines = text.Split('\n', StringSplitOptions.None);
        var result = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                result.AppendLine();
                continue;
            }

            if (trimmedLine == "---")
            {
                result.AppendLine("\n---\n");
                continue;
            }

            if (IsLikelyHeader(trimmedLine))
            {
                result.AppendLine($"## {trimmedLine}");
                result.AppendLine();
                continue;
            }

            if (IsListItem(trimmedLine))
            {
                result.AppendLine(ConvertListItem(trimmedLine));
                continue;
            }

            result.AppendLine(trimmedLine);
        }

        return result.ToString().Trim();
    }

    private static bool IsLikelyHeader(string line)
    {
        if (line.Length > 80 || line.EndsWith('.') || line.EndsWith(','))
            return false;

        var upperCount = line.Count(char.IsUpper);
        var lowerCount = line.Count(char.IsLower);

        return upperCount > lowerCount && upperCount > 2;
    }

    private static bool IsListItem(string line)
    {
        if (line.StartsWith("•") || line.StartsWith("-") || line.StartsWith("*"))
            return true;

        var match = System.Text.RegularExpressions.Regex.Match(line, @"^\d+\.?\s+");
        return match.Success;
    }

    private static string ConvertListItem(string line)
    {
        if (line.StartsWith("•"))
            return line.Replace("•", "-");

        var match = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+)\.?\s+(.*)");
        return match.Success ? $"{match.Groups[1].Value}. {match.Groups[2].Value}" : line;
    }

    private static string? ExtractTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines.Take(10))
        {
            var trimmedLine = line.Trim();

            if (trimmedLine.Length > 5 && trimmedLine.Length < 100 &&
                !trimmedLine.Contains("Page ") &&
                !trimmedLine.All(char.IsDigit))
            {
                return trimmedLine;
            }
        }

        return null;
    }

    internal interface IPdfTextExtractor
    {
        Task<IReadOnlyList<PdfPageText>> ExtractTextAsync(string pdfPath, CancellationToken cancellationToken);
    }

    internal interface IPdfImageRenderer
    {
        Task<IReadOnlyList<string>> RenderImagesAsync(string pdfPath, CancellationToken cancellationToken);
    }

    internal sealed record PdfPageText(int PageNumber, string Text);

    private sealed class PdfPigTextExtractor : IPdfTextExtractor
    {
        public Task<IReadOnlyList<PdfPageText>> ExtractTextAsync(string pdfPath, CancellationToken cancellationToken)
        {
            var pages = new List<PdfPageText>();

            using var pdfDocument = PdfDocument.Open(pdfPath);

            for (var pageNumber = 1; pageNumber <= pdfDocument.NumberOfPages; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = pdfDocument.GetPage(pageNumber);
                var pageText = page.Text;

                if (string.IsNullOrWhiteSpace(pageText))
                {
                    continue;
                }

                pages.Add(new PdfPageText(pageNumber, pageText));
            }

            return Task.FromResult<IReadOnlyList<PdfPageText>>(pages);
        }
    }

    private sealed class PdfToImageRenderer : IPdfImageRenderer
    {
        public Task<IReadOnlyList<string>> RenderImagesAsync(string pdfPath, CancellationToken cancellationToken)
        {
            if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ||
                  OperatingSystem.IsMacCatalyst() || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()))
            {
                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            }

            return RenderOnSupportedPlatformsAsync(pdfPath, cancellationToken);
        }

        [SupportedOSPlatform("windows")]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        [SupportedOSPlatform("maccatalyst")]
        [SupportedOSPlatform("android")]
        [SupportedOSPlatform("ios")]
        private static Task<IReadOnlyList<string>> RenderOnSupportedPlatformsAsync(string pdfPath, CancellationToken cancellationToken)
        {
            var images = new List<string>();
            var options = new RenderOptions
            {
                Dpi = 144,
                WithAnnotations = true,
                WithAspectRatio = true,
                AntiAliasing = PdfAntiAliasing.All,
            };

#pragma warning disable CA1416
            var pdfBytes = File.ReadAllBytes(pdfPath);

            foreach (var bitmap in ToImages(pdfBytes, password: null, options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var bmp = bitmap;
                using var data = bmp.Encode(SKEncodedImageFormat.Png, quality: 90);
                if (data is null)
                {
                    continue;
                }

                images.Add(Convert.ToBase64String(data.Span));
            }
#pragma warning restore CA1416

            return Task.FromResult<IReadOnlyList<string>>(images);
        }
    }
}
