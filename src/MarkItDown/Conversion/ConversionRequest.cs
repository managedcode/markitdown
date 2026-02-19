using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MarkItDown.Converters;
using MarkItDown.Intelligence;

namespace MarkItDown;

/// <summary>
/// Describes per-invocation options for <see cref="MarkItDownClient"/> conversions.
/// </summary>
public sealed class ConversionRequest(
    IntelligenceOverrides intelligence,
    PipelineExecutionOptions pipeline,
    Func<SegmentOptions, SegmentOptions>? segmentConfigurator,
    IReadOnlyDictionary<string, string> metadata)
{
    private static readonly ReadOnlyDictionary<string, string> EmptyMetadata = new(new Dictionary<string, string>());

    public static ConversionRequest Default { get; } = new ConversionRequest(
        IntelligenceOverrides.Empty,
        PipelineExecutionOptions.Default,
        segmentConfigurator: null,
        EmptyMetadata);

    public IntelligenceOverrides Intelligence { get; } = intelligence ?? IntelligenceOverrides.Empty;

    public PipelineExecutionOptions Pipeline { get; } = pipeline ?? PipelineExecutionOptions.Default;

    internal Func<SegmentOptions, SegmentOptions>? SegmentConfigurator { get; } = segmentConfigurator;

    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata ?? EmptyMetadata;

    public static ConversionRequest FromConfiguration(Action<ConversionRequestBuilder> configure)
    {
        if (configure is null)
        {
            return Default;
        }

        var builder = new ConversionRequestBuilder();
        configure(builder);
        return builder.Build();
    }
}

public sealed class ConversionRequestBuilder
{
    private DocumentIntelligenceRequest? document;
    private ImageUnderstandingRequest? image;
    private MediaTranscriptionRequest? media;
    private PipelineExecutionOptions pipeline = PipelineExecutionOptions.Default;
    private readonly Dictionary<string, string> metadata = new(StringComparer.OrdinalIgnoreCase);
    private Func<SegmentOptions, SegmentOptions>? segmentConfigurator;

    public ConversionRequestBuilder UseDocumentIntelligence(DocumentIntelligenceRequest request)
    {
        document = request ?? throw new ArgumentNullException(nameof(request));
        return this;
    }

    public ConversionRequestBuilder UseAzureDocumentIntelligence(Action<AzureDocumentIntelligenceRequestBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new AzureDocumentIntelligenceRequestBuilder();
        configure(builder);
        document = builder.Build();
        return this;
    }

    public ConversionRequestBuilder UseGoogleDocumentIntelligence(Action<GoogleDocumentIntelligenceRequestBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new GoogleDocumentIntelligenceRequestBuilder();
        configure(builder);
        document = builder.Build();
        return this;
    }

    public ConversionRequestBuilder UseAwsDocumentIntelligence(Action<AwsDocumentIntelligenceRequestBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new AwsDocumentIntelligenceRequestBuilder();
        configure(builder);
        document = builder.Build();
        return this;
    }

    public ConversionRequestBuilder UseImageUnderstanding(ImageUnderstandingRequest request)
    {
        image = request ?? throw new ArgumentNullException(nameof(request));
        return this;
    }

    public ConversionRequestBuilder UseMediaTranscription(MediaTranscriptionRequest request)
    {
        media = request ?? throw new ArgumentNullException(nameof(request));
        return this;
    }

    public ConversionRequestBuilder ConfigurePipeline(Action<PipelineExecutionOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new PipelineExecutionOptionsBuilder(pipeline);
        configure(builder);
        pipeline = builder.Build();
        return this;
    }

    public ConversionRequestBuilder UseSegments(SegmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        segmentConfigurator = _ => options;
        return this;
    }

    public ConversionRequestBuilder ConfigureSegments(Func<SegmentOptions, SegmentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (segmentConfigurator is null)
        {
            segmentConfigurator = configure;
        }
        else
        {
            var existing = segmentConfigurator;
            segmentConfigurator = baseline => configure(existing(baseline));
        }

        return this;
    }

    public ConversionRequestBuilder UsePdfConversionMode(PdfConversionMode mode)
        => ConfigureSegments(segments =>
        {
            var pdf = segments.Pdf with
            {
                TreatPagesAsImages = mode == PdfConversionMode.RenderedPageOcr
            };

            return segments with { Pdf = pdf };
        });

    public ConversionRequestBuilder WithMetadata(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        metadata[key] = value ?? string.Empty;
        return this;
    }

    internal ConversionRequest Build()
    {
        var metadataSnapshot = metadata.Count == 0
            ? ConversionRequest.Default.Metadata
            : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(metadata));

        return new ConversionRequest(
            new IntelligenceOverrides(document, image, media),
            pipeline,
            segmentConfigurator,
            metadataSnapshot);
    }
}

public sealed record IntelligenceOverrides(
    DocumentIntelligenceRequest? Document,
    ImageUnderstandingRequest? Image,
    MediaTranscriptionRequest? Media)
{
    public static IntelligenceOverrides Empty { get; } = new IntelligenceOverrides(null, null, null);
}

public sealed class PipelineExecutionOptions
{
    public static PipelineExecutionOptions Default { get; } = new PipelineExecutionOptions(
        enableParallelConverterEvaluation: false,
        maxParallelConverterTasks: Environment.ProcessorCount,
        bufferSegmentSize: 64 * 1024);

    public PipelineExecutionOptions(
        bool enableParallelConverterEvaluation,
        int maxParallelConverterTasks,
        int bufferSegmentSize)
    {
        EnableParallelConverterEvaluation = enableParallelConverterEvaluation;
        MaxParallelConverterTasks = Math.Max(1, maxParallelConverterTasks);
        BufferSegmentSize = Math.Clamp(bufferSegmentSize, 4 * 1024, 4 * 1024 * 1024);
    }

    public bool EnableParallelConverterEvaluation { get; }

    public int MaxParallelConverterTasks { get; }

    public int BufferSegmentSize { get; }
}

public sealed class PipelineExecutionOptionsBuilder
{
    private bool enableParallel;
    private int maxTasks;
    private int bufferSegmentSize;

    public PipelineExecutionOptionsBuilder(PipelineExecutionOptions baseline)
    {
        baseline ??= PipelineExecutionOptions.Default;
        enableParallel = baseline.EnableParallelConverterEvaluation;
        maxTasks = baseline.MaxParallelConverterTasks;
        bufferSegmentSize = baseline.BufferSegmentSize;
    }

    public PipelineExecutionOptionsBuilder EnableParallelConverterEvaluation(bool enabled = true)
    {
        enableParallel = enabled;
        return this;
    }

    public PipelineExecutionOptionsBuilder WithMaxParallelConverterTasks(int maxDegreeOfParallelism)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDegreeOfParallelism);

        maxTasks = maxDegreeOfParallelism;
        return this;
    }

    public PipelineExecutionOptionsBuilder WithBufferSegmentSize(int bytes)
    {
        bufferSegmentSize = bytes;
        return this;
    }

    internal PipelineExecutionOptions Build()
    {
        return new PipelineExecutionOptions(enableParallel, maxTasks, bufferSegmentSize);
    }
}
