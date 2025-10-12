using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Amazon;
using Amazon.Runtime;
using Amazon.Textract;
using Amazon.Textract.Model;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using System.Diagnostics.CodeAnalysis;

namespace MarkItDown.Intelligence.Providers.Aws;

/// <summary>
/// AWS Textract implementation of <see cref="IDocumentIntelligenceProvider"/>.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class AwsDocumentIntelligenceProvider : IDocumentIntelligenceProvider, IDisposable
{
    private readonly IAmazonTextract _client;
    private readonly AwsDocumentIntelligenceOptions _options;
    private bool _disposed;

    public AwsDocumentIntelligenceProvider(AwsDocumentIntelligenceOptions options, IAmazonTextract? client = null)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _options = options;
        _client = client ?? CreateClient(options);
    }

    public async Task<DocumentIntelligenceResult?> AnalyzeAsync(Stream stream, StreamInfo streamInfo, DocumentIntelligenceRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        await using var handle = await DiskBufferHandle.FromStreamAsync(stream, streamInfo.Extension, bufferSize: 256 * 1024, onChunkWritten: null, cancellationToken).ConfigureAwait(false);
        var payload = await File.ReadAllBytesAsync(handle.FilePath, cancellationToken).ConfigureAwait(false);

        var featureTypes = ResolveFeatureTypes(request?.Aws);
        using var documentStream = new MemoryStream(payload, writable: false);

        var analyzeRequest = new AnalyzeDocumentRequest
        {
            Document = new Document
            {
                Bytes = documentStream
            },
            FeatureTypes = featureTypes
        };

        var (client, ownsClient) = ResolveClient(request?.Aws);

        try
        {
            var response = await client.AnalyzeDocumentAsync(analyzeRequest, cancellationToken).ConfigureAwait(false);
            if (response.Blocks.Count == 0)
            {
                return null;
            }

            var blockMap = response.Blocks.ToDictionary(b => b.Id ?? string.Empty, b => b);
            var tables = new List<DocumentTableResult>();
            var pages = new List<DocumentPageResult>();

            var pageNumbers = response.Blocks
                .Where(b => string.Equals(b.BlockType, BlockType.PAGE, StringComparison.OrdinalIgnoreCase) && b.Page.HasValue)
                .Select(b => b.Page!.Value)
                .Distinct()
                .OrderBy(n => n)
                .ToArray();

            foreach (var pageNumber in pageNumbers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lines = response.Blocks
                    .Where(b => b.Page == pageNumber && string.Equals(b.BlockType, BlockType.LINE, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(b => b.Geometry?.BoundingBox?.Top ?? 0)
                    .ThenBy(b => b.Geometry?.BoundingBox?.Left ?? 0)
                    .Select(b => b.Text)
                    .Where(static t => !string.IsNullOrWhiteSpace(t))
                    .ToArray();

                var text = string.Join(Environment.NewLine, lines);

                var pageTableIndices = new List<int>();

                var pageTables = response.Blocks
                    .Where(b => b.Page == pageNumber && string.Equals(b.BlockType, BlockType.TABLE, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var tableBlock in pageTables)
                {
                    var converted = ConvertTable(tableBlock, blockMap, pageNumber);
                    pageTableIndices.Add(tables.Count);
                    tables.Add(converted);
                }

                var metadata = new Dictionary<string, string>
                {
                    [MetadataKeys.Page] = pageNumber.ToString(CultureInfo.InvariantCulture)
                };

                pages.Add(new DocumentPageResult(pageNumber, text, pageTableIndices, metadata: metadata));
            }

            return new DocumentIntelligenceResult(pages, tables, images: Array.Empty<DocumentImageResult>());
        }
        finally
        {
            if (ownsClient)
            {
                (client as IDisposable)?.Dispose();
            }
        }
    }

    private static List<string> ResolveFeatureTypes(AwsDocumentIntelligenceOverrides? overrides)
    {
        if (overrides?.FeatureTypes is { Count: > 0 })
        {
            return overrides.FeatureTypes.Select(ft => ft.ToUpperInvariant()).ToList();
        }

        return new List<string> { FeatureType.TABLES.ToString(), FeatureType.FORMS.ToString() };
    }

    private (IAmazonTextract Client, bool OwnsClient) ResolveClient(AwsDocumentIntelligenceOverrides? overrides)
    {
        if (overrides is null || string.IsNullOrWhiteSpace(overrides.Region))
        {
            return (_client, false);
        }

        var effectiveOptions = new AwsDocumentIntelligenceOptions
        {
            Credentials = _options.Credentials,
            AccessKeyId = _options.AccessKeyId,
            SecretAccessKey = _options.SecretAccessKey,
            SessionToken = _options.SessionToken,
            Region = overrides.Region
        };

        return (CreateClient(effectiveOptions), true);
    }

    private static DocumentTableResult ConvertTable(Block tableBlock, IDictionary<string, Block> blockMap, int pageNumber)
    {
        var cellBlocks = GetChildBlocks(tableBlock, blockMap)
            .Where(b => string.Equals(b.BlockType, BlockType.CELL, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (cellBlocks.Count == 0)
        {
            return new DocumentTableResult(pageNumber, Array.Empty<IReadOnlyList<string>>());
        }

        var rowCount = 0;
        var columnCount = 0;

        foreach (var cell in cellBlocks)
        {
            var lastRow = (cell.RowIndex ?? 1) + (cell.RowSpan ?? 1) - 1;
            if (lastRow > rowCount)
            {
                rowCount = lastRow;
            }

            var lastColumn = (cell.ColumnIndex ?? 1) + (cell.ColumnSpan ?? 1) - 1;
            if (lastColumn > columnCount)
            {
                columnCount = lastColumn;
            }
        }

        rowCount = Math.Max(1, rowCount);
        columnCount = Math.Max(1, columnCount);

        var rows = new string[rowCount][];
        for (var r = 0; r < rowCount; r++)
        {
            rows[r] = Enumerable.Repeat(string.Empty, columnCount).ToArray();
        }

        foreach (var cell in cellBlocks)
        {
            var text = ExtractCellText(cell, blockMap);
            var rowIndex = (cell.RowIndex ?? 1) - 1;
            var columnIndex = (cell.ColumnIndex ?? 1) - 1;
            var rowSpan = cell.RowSpan ?? 1;
            var columnSpan = cell.ColumnSpan ?? 1;

            for (var r = 0; r < rowSpan; r++)
            {
                for (var c = 0; c < columnSpan; c++)
                {
                    var targetRow = rowIndex + r;
                    var targetColumn = columnIndex + c;
                    if (targetRow >= 0 && targetRow < rows.Length && targetColumn >= 0 && targetColumn < rows[targetRow].Length)
                    {
                        rows[targetRow][targetColumn] = text;
                    }
                }
            }
        }

        var convertedRows = rows.Select(row => (IReadOnlyList<string>)Array.AsReadOnly(row)).ToList();

        var metadata = new Dictionary<string, string>
        {
            [MetadataKeys.RowCount] = rowCount.ToString(CultureInfo.InvariantCulture),
            [MetadataKeys.ColumnCount] = columnCount.ToString(CultureInfo.InvariantCulture)
        };

        return new DocumentTableResult(pageNumber, convertedRows, metadata);
    }

    private static IEnumerable<Block> GetChildBlocks(Block block, IDictionary<string, Block> blockMap)
    {
        if (block.Relationships is null)
        {
            yield break;
        }

        foreach (var relationship in block.Relationships)
        {
            if (!string.Equals(relationship.Type, RelationshipType.CHILD, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var id in relationship.Ids)
            {
                if (!string.IsNullOrEmpty(id) && blockMap.TryGetValue(id, out var child))
                {
                    yield return child;
                }
            }
        }
    }

    private static string ExtractCellText(Block cell, IDictionary<string, Block> blockMap)
    {
        if (cell.Relationships is null || cell.Relationships.Count == 0)
        {
            return cell.Text ?? string.Empty;
        }

        var words = new List<string>();
        foreach (var relationship in cell.Relationships)
        {
            if (!string.Equals(relationship.Type, RelationshipType.CHILD, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var id in relationship.Ids)
            {
                if (!blockMap.TryGetValue(id, out var block))
                {
                    continue;
                }

                if (string.Equals(block.BlockType, BlockType.WORD, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(block.Text))
                    {
                        words.Add(block.Text);
                    }
                }
                else if (string.Equals(block.BlockType, BlockType.SELECTION_ELEMENT, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(block.SelectionStatus, SelectionStatus.SELECTED, StringComparison.OrdinalIgnoreCase))
                    {
                        words.Add("[X]");
                    }
                }
            }
        }

        return words.Count == 0 ? cell.Text ?? string.Empty : string.Join(' ', words);
    }

    private static IAmazonTextract CreateClient(AwsDocumentIntelligenceOptions options)
    {
        var region = ResolveRegion(options.Region);

        if (options.Credentials is not null)
        {
            return new AmazonTextractClient(options.Credentials, region);
        }

        if (!string.IsNullOrWhiteSpace(options.AccessKeyId) && !string.IsNullOrWhiteSpace(options.SecretAccessKey))
        {
            AWSCredentials credentials = string.IsNullOrWhiteSpace(options.SessionToken)
                ? new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey)
                : new SessionAWSCredentials(options.AccessKeyId, options.SecretAccessKey, options.SessionToken);

            return new AmazonTextractClient(credentials, region);
        }

        return new AmazonTextractClient(region);
    }

    private static RegionEndpoint ResolveRegion(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? RegionEndpoint.USEast1 : RegionEndpoint.GetBySystemName(value);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
    }
}
