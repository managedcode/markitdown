using System.Globalization;
using System.Linq;
using System.Text;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Identity;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using ManagedCode.MimeTypes;
using System.Diagnostics.CodeAnalysis;

namespace MarkItDown.Intelligence.Providers.Azure;

/// <summary>
/// Azure implementation of <see cref="IDocumentIntelligenceProvider"/> built on top of Document Intelligence (Form Recognizer).
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class AzureDocumentIntelligenceProvider : IDocumentIntelligenceProvider
{
    private readonly DocumentAnalysisClient _client;
    private readonly AzureDocumentIntelligenceOptions _options;

    public AzureDocumentIntelligenceProvider(AzureDocumentIntelligenceOptions options, DocumentAnalysisClient? client = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new ArgumentException("Azure Document Intelligence endpoint must be provided.", nameof(options));
        }

        if (client is not null)
        {
            _client = client;
        }
        else
        {
            var endpoint = new Uri(options.Endpoint);

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                _client = new DocumentAnalysisClient(endpoint, new AzureKeyCredential(options.ApiKey));
            }
            else
            {
                _client = new DocumentAnalysisClient(endpoint, new DefaultAzureCredential());
            }
        }
    }

    public async Task<DocumentIntelligenceResult?> AnalyzeAsync(Stream stream, StreamInfo streamInfo, DocumentIntelligenceRequest? request = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!IsSupportedStream(streamInfo))
        {
            return null;
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        var effectiveModelId = request?.Azure?.ModelId;
        if (string.IsNullOrWhiteSpace(effectiveModelId))
        {
            effectiveModelId = _options.ModelId;
        }

        var client = ResolveClient(request?.Azure);

        AnalyzeDocumentOperation operation;
        try
        {
            operation = await client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                effectiveModelId,
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (IsUnsupportedContentError(ex))
        {
            return null;
        }

        AnalyzeResult analyzeResult = operation.Value;

        var pages = new List<DocumentPageResult>(analyzeResult.Pages.Count);
        var tables = new List<DocumentTableResult>(analyzeResult.Tables.Count);

        foreach (var table in analyzeResult.Tables)
        {
            tables.Add(ConvertTable(table));
        }

        foreach (var page in analyzeResult.Pages)
        {
            var textBuilder = new StringBuilder();
            foreach (var line in page.Lines)
            {
                textBuilder.AppendLine(line.Content);
            }

            var tableIndices = new List<int>();
            for (var tableIndex = 0; tableIndex < analyzeResult.Tables.Count; tableIndex++)
            {
                if (analyzeResult.Tables[tableIndex].BoundingRegions?.Any(region => region.PageNumber == page.PageNumber) == true)
                {
                    tableIndices.Add(tableIndex);
                }
            }

            var pageMetadata = new Dictionary<string, string>();
            if (page.Width.HasValue)
            {
                pageMetadata[MetadataKeys.Width] = page.Width.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (page.Height.HasValue)
            {
                pageMetadata[MetadataKeys.Height] = page.Height.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (page.Unit.HasValue)
            {
                pageMetadata[MetadataKeys.Unit] = page.Unit.Value.ToString();
            }

            pages.Add(new DocumentPageResult(
                page.PageNumber,
                textBuilder.ToString().TrimEnd(),
                tableIndices,
                metadata: pageMetadata));
        }

        return new DocumentIntelligenceResult(pages, tables, images: Array.Empty<DocumentImageResult>());
    }

    private DocumentAnalysisClient ResolveClient(AzureDocumentIntelligenceOverrides? overrides)
    {
        if (overrides is null || (string.IsNullOrWhiteSpace(overrides.Endpoint) && string.IsNullOrWhiteSpace(overrides.ApiKey)))
        {
            return _client;
        }

        var endpoint = overrides.Endpoint ?? _options.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return _client;
        }

        if (!string.IsNullOrWhiteSpace(overrides.ApiKey))
        {
            return new DocumentAnalysisClient(new Uri(endpoint), new AzureKeyCredential(overrides.ApiKey));
        }

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return new DocumentAnalysisClient(new Uri(endpoint), new AzureKeyCredential(_options.ApiKey));
        }

        return new DocumentAnalysisClient(new Uri(endpoint), new DefaultAzureCredential());
    }

    private static bool IsSupportedStream(StreamInfo streamInfo)
    {
        var extension = streamInfo.Extension?.ToLowerInvariant();
        if (extension is ".pdf" or ".tiff" or ".tif")
        {
            return true;
        }

        var mimeType = streamInfo.ResolveMimeType();
        return string.Equals(mimeType, MimeHelper.PDF, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mimeType, MimeHelper.TIFF, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnsupportedContentError(RequestFailedException ex)
        => string.Equals(ex.ErrorCode, "InvalidRequest", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(ex.ErrorCode, "InvalidContent", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("Invalid content", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("unsupported", StringComparison.OrdinalIgnoreCase);

    private static DocumentTableResult ConvertTable(DocumentTable table)
    {
        var rows = new string[table.RowCount][];
        for (var i = 0; i < table.RowCount; i++)
        {
            rows[i] = Enumerable.Repeat(string.Empty, table.ColumnCount).ToArray();
        }

        foreach (var cell in table.Cells)
        {
            var value = cell.Content?.Trim() ?? string.Empty;

            for (var rowOffset = 0; rowOffset < cell.RowSpan; rowOffset++)
            {
                for (var columnOffset = 0; columnOffset < cell.ColumnSpan; columnOffset++)
                {
                    var targetRow = cell.RowIndex + rowOffset;
                    var targetColumn = cell.ColumnIndex + columnOffset;
                    if (targetRow < rows.Length && targetColumn < rows[targetRow].Length)
                    {
                        rows[targetRow][targetColumn] = value;
                    }
                }
            }
        }

        var convertedRows = rows.Select(static r => (IReadOnlyList<string>)Array.AsReadOnly(r)).ToList();

        var metadata = new Dictionary<string, string>
        {
            [MetadataKeys.RowCount] = table.RowCount.ToString(CultureInfo.InvariantCulture),
            [MetadataKeys.ColumnCount] = table.ColumnCount.ToString(CultureInfo.InvariantCulture)
        };

        if (table.BoundingRegions?.Count > 0)
        {
            metadata[MetadataKeys.Page] = table.BoundingRegions[0].PageNumber.ToString(CultureInfo.InvariantCulture);
        }

        var pageNumber = table.BoundingRegions is not null && table.BoundingRegions.Count > 0
            ? table.BoundingRegions[0].PageNumber
            : 1;

        return new DocumentTableResult(pageNumber, convertedRows, metadata);
    }
}
