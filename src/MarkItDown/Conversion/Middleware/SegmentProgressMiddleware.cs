using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MarkItDown.Conversion.Middleware;

/// <summary>
/// Reports segment-level progress once extraction has produced ordered segments.
/// </summary>
public sealed class SegmentProgressMiddleware : IConversionMiddleware
{
    public Task InvokeAsync(ConversionPipelineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var progress = ConversionContextAccessor.Current?.Progress;
        if (progress is null)
        {
            return Task.CompletedTask;
        }

        var segments = context.Segments;
        if (segments.Count == 0)
        {
            return Task.CompletedTask;
        }

        var total = segments.Count;
        var detailLevel = context.ProgressDetail;

        var pageCount = 0;
        var imageCount = 0;
        var tableCount = 0;

        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var segment = segments[i];
            if (segment is null)
            {
                continue;
            }

            switch (segment.Type)
            {
                case SegmentType.Page:
                    pageCount++;
                    break;
                case SegmentType.Image:
                    imageCount++;
                    break;
                case SegmentType.Table:
                    tableCount++;
                    break;
            }

            if (detailLevel == ProgressDetailLevel.Detailed)
            {
                var details = BuildDetails(segment, i);
                progress.Report(new ConversionProgress("segments", i + 1, total, details));
                context.Logger?.LogDebug("Segments {Completed}/{Total} processed {Details}", i + 1, total, details ?? string.Empty);
            }
        }

        if (detailLevel != ProgressDetailLevel.Detailed)
        {
            var summary = $"segments={total} pages={pageCount} images={context.Artifacts.Images.Count} tables={tableCount}";
            progress.Report(new ConversionProgress("segments", total, total, summary));
            context.Logger?.LogDebug("Segment summary {Summary}", summary);
        }

        return Task.CompletedTask;
    }

    private static string? BuildDetails(DocumentSegment segment, int index)
    {
        var number = segment.Number ?? index + 1;
        if (!string.IsNullOrWhiteSpace(segment.Label))
        {
            return $"{segment.Type}:{number} {segment.Label}";
        }

        if (!string.IsNullOrWhiteSpace(segment.Source))
        {
            return $"{segment.Type}:{number} {segment.Source}";
        }

        return $"{segment.Type}:{number}";
    }
}
