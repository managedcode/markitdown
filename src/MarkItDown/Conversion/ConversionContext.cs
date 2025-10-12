using System;
using System.Threading;
using MarkItDown.Intelligence;

namespace MarkItDown;

/// <summary>
/// Ambient context exposed to converters during a conversion pipeline execution.
/// </summary>
public sealed class ConversionContext
{
    public ConversionContext(
        StreamInfo streamInfo,
        ConversionRequest request,
        IntelligenceProviderHub providers,
        SegmentOptions segments,
        PipelineExecutionOptions pipelineOptions,
        ArtifactStorageOptions storageOptions,
        IProgress<ConversionProgress>? progress,
        ProgressDetailLevel progressDetail)
    {
        StreamInfo = streamInfo;
        Request = request ?? ConversionRequest.Default;
        Providers = providers ?? new IntelligenceProviderHub(null, null, null, NullAiModelProvider.Instance);
        Storage = storageOptions ?? ArtifactStorageOptions.Default;

        var baselineSegments = segments ?? SegmentOptions.Default;
        if (Request.SegmentConfigurator is not null)
        {
            try
            {
                baselineSegments = Request.SegmentConfigurator(baselineSegments) ?? baselineSegments;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to apply segment configuration overrides.", ex);
            }
        }

        Segments = baselineSegments;
        PipelineOptions = pipelineOptions ?? PipelineExecutionOptions.Default;
        Progress = progress;
        ProgressDetail = progressDetail;
    }

    public StreamInfo StreamInfo { get; }

    public ConversionRequest Request { get; }

    public IntelligenceProviderHub Providers { get; }

    public SegmentOptions Segments { get; }

    public ArtifactStorageOptions Storage { get; }

    public PipelineExecutionOptions PipelineOptions { get; }

    public IProgress<ConversionProgress>? Progress { get; }

    public ProgressDetailLevel ProgressDetail { get; }
}

internal static class ConversionContextAccessor
{
    private static readonly AsyncLocal<ConversionContextScope?> CurrentScope = new();

    public static ConversionContext? Current => CurrentScope.Value?.Context;

    public static ConversionContextScope Push(ConversionContext context)
    {
        var newScope = new ConversionContextScope(context, CurrentScope.Value);
        CurrentScope.Value = newScope;
        return newScope;
    }

    public static void Pop(ConversionContextScope? scope)
    {
        if (scope is null)
        {
            return;
        }

        if (CurrentScope.Value != scope)
        {
            return;
        }

        CurrentScope.Value = scope.Previous;
    }
}

internal sealed class ConversionContextScope : IDisposable
{
    internal ConversionContextScope(ConversionContext context, ConversionContextScope? previous)
    {
        Context = context;
        Previous = previous;
    }

    public ConversionContext Context { get; }

    public ConversionContextScope? Previous { get; }

    public void Dispose()
    {
        ConversionContextAccessor.Pop(this);
    }
}
