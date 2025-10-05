using System.Collections.Generic;
using System.Linq;
using System.Text;
using Google.Api.Gax.Grpc;
using Google.Cloud.DocumentAI.V1;
using Google.Protobuf;
using MarkItDown;
using ManagedCode.MimeTypes;
using MarkItDown.Intelligence.Models;

namespace MarkItDown.Intelligence.Providers.Google;

/// <summary>
/// Google Document AI implementation of <see cref="IDocumentIntelligenceProvider"/>.
/// </summary>
public sealed class GoogleDocumentIntelligenceProvider : IDocumentIntelligenceProvider
{
    private readonly DocumentProcessorServiceClient _client;
    private readonly GoogleDocumentIntelligenceOptions _options;
    private readonly string _processorName;

    public GoogleDocumentIntelligenceProvider(GoogleDocumentIntelligenceOptions options, DocumentProcessorServiceClient? client = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.ProjectId))
        {
            throw new ArgumentException("Google Document AI project id must be provided.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Location))
        {
            throw new ArgumentException("Google Document AI location must be provided.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.ProcessorId))
        {
            throw new ArgumentException("Google Document AI processor id must be provided.", nameof(options));
        }

        _processorName = ProcessorName.FromProjectLocationProcessor(options.ProjectId, options.Location, options.ProcessorId).ToString();

        if (client is not null)
        {
            _client = client;
        }
        else
        {
            var builder = new DocumentProcessorServiceClientBuilder
            {
                Endpoint = $"{options.Location}-documentai.googleapis.com"
            };

            if (options.Credential is not null)
            {
                builder.Credential = options.Credential;
            }
            else if (!string.IsNullOrWhiteSpace(options.JsonCredentials))
            {
                builder.JsonCredentials = options.JsonCredentials;
            }
            else if (!string.IsNullOrWhiteSpace(options.CredentialsPath))
            {
                builder.CredentialsPath = options.CredentialsPath;
            }

            _client = builder.Build();
        }
    }

    public async Task<DocumentIntelligenceResult?> AnalyzeAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        memory.Position = 0;

        var mimeType = !string.IsNullOrWhiteSpace(streamInfo.MimeType)
            ? streamInfo.MimeType!
            : MimeHelper.GetMimeType(streamInfo.Extension ?? string.Empty);

        var request = new ProcessRequest
        {
            Name = _processorName,
            RawDocument = new RawDocument
            {
                Content = ByteString.CopyFrom(memory.ToArray()),
                MimeType = string.IsNullOrWhiteSpace(mimeType) ? "application/pdf" : mimeType
            }
        };

        var callSettings = CallSettings.FromCancellationToken(cancellationToken);
        var response = await _client.ProcessDocumentAsync(request, callSettings).ConfigureAwait(false);
        var document = response.Document;
        if (document is null)
        {
            return null;
        }

        var tables = new List<DocumentTableResult>();
        var pages = new List<DocumentPageResult>();

        foreach (var page in document.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageText = ExtractText(document, page.Layout?.TextAnchor);
            var tableIndices = new List<int>();

            foreach (var table in page.Tables)
            {
                var converted = ConvertTable(document, table, page.PageNumber);
                tableIndices.Add(tables.Count);
                tables.Add(converted);
            }

            var metadata = new Dictionary<string, string>();
            if (page.Dimension is not null)
            {
                metadata[MetadataKeys.Width] = page.Dimension.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
                metadata[MetadataKeys.Height] = page.Dimension.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
                metadata[MetadataKeys.Unit] = page.Dimension.Unit;
            }

            pages.Add(new DocumentPageResult(
                page.PageNumber,
                pageText,
                tableIndices,
                metadata: metadata));
        }

        return new DocumentIntelligenceResult(pages, tables, images: Array.Empty<DocumentImageResult>());
    }

    private static string ExtractText(Document document, Document.Types.TextAnchor? anchor)
    {
        if (document is null || string.IsNullOrEmpty(document.Text) || anchor is null || anchor.TextSegments.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var segment in anchor.TextSegments)
        {
            var startIndex = (int?)segment.StartIndex ?? 0;
            var endIndex = (int?)segment.EndIndex ?? document.Text.Length;
            if (startIndex < 0 || endIndex <= startIndex || endIndex > document.Text.Length)
            {
                continue;
            }

            builder.Append(document.Text.Substring(startIndex, endIndex - startIndex));
        }

        return builder.ToString().Trim();
    }

    private static DocumentTableResult ConvertTable(Document document, Document.Types.Page.Types.Table table, int pageNumber)
    {
        var rows = new List<IReadOnlyList<string>>();

        if (table.HeaderRows.Count > 0)
        {
            foreach (var headerRow in table.HeaderRows)
            {
                rows.Add(headerRow.Cells.Select(cell => ExtractText(document, cell.Layout?.TextAnchor)).ToList());
            }
        }

        foreach (var bodyRow in table.BodyRows)
        {
            rows.Add(bodyRow.Cells.Select(cell => ExtractText(document, cell.Layout?.TextAnchor)).ToList());
        }

        if (rows.Count == 0)
        {
            rows.Add(Array.Empty<string>());
        }

        var metadata = new Dictionary<string, string>
        {
            [MetadataKeys.RowCount] = (table.HeaderRows.Count + table.BodyRows.Count).ToString(System.Globalization.CultureInfo.InvariantCulture),
            [MetadataKeys.ColumnCount] = (rows.Count > 0 ? rows.Max(r => r.Count) : 0).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        return new DocumentTableResult(pageNumber, rows, metadata);
    }
}
