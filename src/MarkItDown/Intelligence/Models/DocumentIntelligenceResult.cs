using System.Collections.ObjectModel;

namespace MarkItDown.Intelligence.Models;

/// <summary>
/// Represents the outcome of running a document through an intelligence provider such as Azure Document Intelligence.
/// </summary>
public sealed class DocumentIntelligenceResult
{
    public DocumentIntelligenceResult(
        IReadOnlyList<DocumentPageResult> pages,
        IReadOnlyList<DocumentTableResult>? tables = null,
        IReadOnlyList<DocumentImageResult>? images = null)
    {
        Pages = pages ?? throw new ArgumentNullException(nameof(pages));
        Tables = tables ?? Array.Empty<DocumentTableResult>();
        Images = images ?? Array.Empty<DocumentImageResult>();
    }

    /// <summary>
    /// Pages returned from the provider in reading order.
    /// </summary>
    public IReadOnlyList<DocumentPageResult> Pages { get; }

    /// <summary>
    /// Tables detected within the document. Indexes correspond to entries referenced from <see cref="DocumentPageResult.TableIndices"/>.
    /// </summary>
    public IReadOnlyList<DocumentTableResult> Tables { get; }

    /// <summary>
    /// Images rendered or extracted from the document.
    /// </summary>
    public IReadOnlyList<DocumentImageResult> Images { get; }
}

/// <summary>
/// Describes a textual representation of a single page.
/// </summary>
public sealed class DocumentPageResult
{
    public DocumentPageResult(
        int pageNumber,
        string text,
        IReadOnlyList<int>? tableIndices = null,
        IReadOnlyList<int>? imageIndices = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        PageNumber = pageNumber;
        Text = text ?? string.Empty;
        TableIndices = tableIndices ?? Array.Empty<int>();
        ImageIndices = imageIndices ?? Array.Empty<int>();
        Metadata = metadata ?? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    }

    public int PageNumber { get; }

    public string Text { get; }

    public IReadOnlyList<int> TableIndices { get; }

    public IReadOnlyList<int> ImageIndices { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}

/// <summary>
/// Represents a table extracted from the document.
/// </summary>
public sealed class DocumentTableResult
{
    public DocumentTableResult(
        int pageNumber,
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        PageNumber = pageNumber;
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        Metadata = metadata ?? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    }

    public int PageNumber { get; }

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}

/// <summary>
/// Represents an image embedded inside the document and the associated understanding metadata.
/// </summary>
public sealed class DocumentImageResult
{
    public DocumentImageResult(
        int pageNumber,
        byte[] content,
        string contentType,
        string? caption = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        PageNumber = pageNumber;
        Content = content ?? throw new ArgumentNullException(nameof(content));
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        Caption = caption;
        Metadata = metadata ?? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    }

    public int PageNumber { get; }

    public byte[] Content { get; }

    public string ContentType { get; }

    public string? Caption { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}
