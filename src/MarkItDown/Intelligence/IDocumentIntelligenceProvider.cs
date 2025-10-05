using MarkItDown.Intelligence.Models;

namespace MarkItDown.Intelligence;

/// <summary>
/// Provides structured document analysis (pages, tables, embedded images) from cloud services.
/// </summary>
public interface IDocumentIntelligenceProvider
{
    /// <summary>
    /// Analyze the supplied document stream. Returns <c>null</c> if the provider is unavailable or disabled.
    /// </summary>
    Task<DocumentIntelligenceResult?> AnalyzeAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default);
}
