using System.Linq;

namespace MarkItDown.Cli;

internal sealed record ConversionResult(
    string Input,
    string? Output,
    bool Success,
    string? Error,
    int SegmentCount,
    string? Title,
    int PageCount,
    int ImageCount,
    int TableCount,
    int AttachmentCount,
    string? AttachmentSummary);

internal sealed class ConversionSummary
{
    public ConversionSummary(IReadOnlyList<ConversionResult> results)
    {
        Results = results;
    }

    public IReadOnlyList<ConversionResult> Results { get; }

    public int SuccessCount => Results.Count(r => r.Success);

    public int FailureCount => Results.Count - SuccessCount;

    public int TotalPages => Results.Sum(r => r.PageCount);

    public int TotalImages => Results.Sum(r => r.ImageCount);

    public int TotalTables => Results.Sum(r => r.TableCount);

    public int TotalAttachments => Results.Sum(r => r.AttachmentCount);
}

internal readonly record struct ConversionProgress(int Processed, int Total, string Current);
