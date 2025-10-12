using System;

namespace MarkItDown;

/// <summary>
/// Represents progress information for a conversion operation.
/// </summary>
public sealed record ConversionProgress(string Stage, int Completed, int Total, string? Details = null)
{
    public double Percent => Total <= 0 ? 0 : Math.Min(100, Math.Max(0, Completed * 100d / Total));
}
