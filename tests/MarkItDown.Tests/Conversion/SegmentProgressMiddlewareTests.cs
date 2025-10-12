using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;
using MarkItDown.Conversion.Middleware;
using MarkItDown.Intelligence;
using Shouldly;
using Xunit;

namespace MarkItDown.Tests.Conversion;

public sealed class SegmentProgressMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WithSegments_ReportsProgressForEachSegment()
    {
        var streamInfo = new StreamInfo(mimeType: "application/pdf", extension: ".pdf", fileName: "sample.pdf");
        var segments = new List<DocumentSegment>
        {
            new DocumentSegment("alpha", SegmentType.Page, number: 1, label: "Page 1"),
            new DocumentSegment("beta", SegmentType.Page, number: 2, label: "Page 2"),
            new DocumentSegment("gamma", SegmentType.Page, number: 3, label: "Page 3")
        };

        var reported = new List<ConversionProgress>();
        var progress = new Collector(reported);

        var context = new ConversionContext(
            streamInfo,
            ConversionRequest.Default,
            new IntelligenceProviderHub(document: null, image: null, media: null, aiModels: NullAiModelProvider.Instance),
            SegmentOptions.Default,
            PipelineExecutionOptions.Default,
            ArtifactStorageOptions.Default,
            progress,
            ProgressDetailLevel.Detailed);

        using var scope = ConversionContextAccessor.Push(context);

        var middleware = new SegmentProgressMiddleware();
        var pipelineContext = new ConversionPipelineContext(streamInfo, new ConversionArtifacts(), segments, NullAiModelProvider.Instance, logger: null, SegmentOptions.Default, ProgressDetailLevel.Detailed);

        await middleware.InvokeAsync(pipelineContext, CancellationToken.None);

        reported.Count.ShouldBe(segments.Count);
        for (var i = 0; i < segments.Count; i++)
        {
            var entry = reported[i];
            entry.Stage.ShouldBe("segments");
            entry.Completed.ShouldBe(i + 1);
            entry.Total.ShouldBe(segments.Count);
        }
    }
}

internal sealed class Collector : IProgress<ConversionProgress>
{
    private readonly IList<ConversionProgress> target;

    public Collector(IList<ConversionProgress> target)
    {
        this.target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public void Report(ConversionProgress value) => target.Add(value);
}
