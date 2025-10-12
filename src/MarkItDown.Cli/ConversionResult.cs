using System.Linq;

namespace MarkItDown.Cli;

internal sealed record ConversionResult(string Input, string? Output, bool Success, string? Error, int SegmentCount);

internal sealed class ConversionSummary
{
    public ConversionSummary(IReadOnlyList<ConversionResult> results)
    {
        Results = results;
    }

    public IReadOnlyList<ConversionResult> Results { get; }

    public int SuccessCount => Results.Count(r => r.Success);

    public int FailureCount => Results.Count - SuccessCount;
}

internal readonly record struct ConversionProgress(int Processed, int Total, string Current);
