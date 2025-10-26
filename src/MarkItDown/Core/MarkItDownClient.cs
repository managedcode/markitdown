using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Core;
using Azure.Identity;
using ManagedCode.MimeTypes;
using Microsoft.Extensions.Logging;
using System.Globalization;
using MarkItDown.Conversion.Middleware;
using MarkItDown.Converters;
using MarkItDown.Intelligence;
using MarkItDown.Conversion.Pipelines;
using MarkItDown.Intelligence.Providers.Aws;
using MarkItDown.Intelligence.Providers.Azure;
using MarkItDown.Intelligence.Providers.Google;
using MarkItDown.YouTube;

namespace MarkItDown;

/// <summary>
/// Main class for converting various file formats to Markdown.
/// An extremely simple text-based document reader, suitable for LLM use.
/// This reader will convert common file-types or webpages to Markdown.
/// </summary>
public sealed class MarkItDownClient : IMarkItDownClient
{
    private readonly List<ConverterRegistration> _converters;
    private readonly ILogger? _logger;
    private readonly HttpClient? _httpClient;
    private readonly MarkItDownOptions _options;
    private readonly IntelligenceProviderHub _intelligenceProviders;
    private readonly IConversionPipeline _conversionPipeline;
    private readonly ActivitySource _activitySource;
    private readonly Counter<long>? _conversionCounter;
    private readonly Counter<long>? _conversionFailureCounter;
    private readonly IYouTubeMetadataProvider _youTubeMetadataProvider;
    private readonly ProgressDetailLevel _progressDetail;

    /// <summary>
    /// Initialize a new instance of <see cref="MarkItDownClient"/>.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic information.</param>
    /// <param name="httpClient">Optional HTTP client for downloading web content.</param>
    public MarkItDownClient(ILogger? logger = null, HttpClient? httpClient = null)
        : this(null, logger, httpClient)
    {
    }

    /// <summary>
    /// Initialize a new instance of <see cref="MarkItDownClient"/> with advanced configuration options.
    /// </summary>
    /// <param name="options">Configuration overrides for the converter. When <see langword="null"/> defaults are used.</param>
    /// <param name="logger">Optional logger for diagnostic information.</param>
    /// <param name="httpClient">Optional HTTP client for downloading web content.</param>
    public MarkItDownClient(MarkItDownOptions? options, ILogger? logger = null, HttpClient? httpClient = null)
    {
        _options = options ?? new MarkItDownOptions();
        _logger = logger ?? _options.LoggerFactory?.CreateLogger<MarkItDownClient>();
        _httpClient = httpClient;
        _converters = [];
        _intelligenceProviders = InitializeIntelligenceProviders();
        _conversionPipeline = BuildConversionPipeline();
        _activitySource = _options.ActivitySource ?? MarkItDownDiagnostics.DefaultActivitySource;
        _progressDetail = _options.ProgressDetail;

        if (_options.EnableTelemetry)
        {
            (_conversionCounter, _conversionFailureCounter) = MarkItDownDiagnostics.ResolveCounters(_options.Meter);
        }
        _youTubeMetadataProvider = _options.YouTubeMetadataProvider ?? new YoutubeExplodeMetadataProvider();

        if (_options.EnableBuiltins)
        {
            RegisterBuiltInConverters();
        }

        if (_options.EnablePlugins)
        {
            // TODO: parity with Python plugin discovery.
            _logger?.LogWarning("Plugin support is not yet available in the .NET port.");
        }
    }

    /// <summary>
    /// Register a custom converter.
    /// </summary>
    /// <param name="converter">The converter to register.</param>
    /// <param name="priority">The priority of the converter. Lower values are tried first.</param>
    public void RegisterConverter(DocumentConverterBase converter, double priority = ConverterPriority.SpecificFileFormat)
    {
        ArgumentNullException.ThrowIfNull(converter);
        
        _converters.Add(new ConverterRegistration(converter, priority));
        
        // Sort by priority to ensure proper order
        _converters.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    /// <summary>
    /// Register a custom converter with the converter's default priority.
    /// </summary>
    /// <param name="converter">The converter to register.</param>
    public void RegisterConverter(DocumentConverterBase converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        RegisterConverter(converter, converter.Priority);
    }

    /// <summary>
    /// Get the list of registered converters.
    /// </summary>
    /// <returns>A read-only list of registered converters.</returns>
    public IReadOnlyList<DocumentConverterBase> GetRegisteredConverters()
    {
        return _converters.Select(r => r.Converter).ToList().AsReadOnly();
    }

    /// <summary>
    /// Convert a file to Markdown.
    /// </summary>
    /// <param name="filePath">Path to the file to convert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The converted Markdown content.</returns>
    public Task<DocumentConverterResult> ConvertAsync(string filePath, CancellationToken cancellationToken = default)
        => ConvertFileInternalAsync(filePath, null, progress: null, ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(string filePath, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken = default)
        => ConvertFileInternalAsync(filePath, null, progress, ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(string filePath, Action<ConversionProgress> progress, CancellationToken cancellationToken = default)
        => ConvertFileInternalAsync(filePath, null, WrapProgress(progress), ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(string filePath, StreamInfo streamInfoOverride, CancellationToken cancellationToken = default)
        => ConvertFileInternalAsync(filePath, streamInfoOverride, progress: null, ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(string filePath, StreamInfo streamInfoOverride, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken = default)
        => ConvertFileInternalAsync(filePath, streamInfoOverride, progress, ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(string filePath, StreamInfo streamInfoOverride, Action<ConversionProgress> progress, CancellationToken cancellationToken = default)
        => ConvertFileInternalAsync(filePath, streamInfoOverride, WrapProgress(progress), ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(string filePath, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertFileInternalAsync(filePath, null, progress: null, ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(string filePath, IProgress<ConversionProgress>? progress, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertFileInternalAsync(filePath, null, progress, ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(string filePath, Action<ConversionProgress> progress, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertFileInternalAsync(filePath, null, WrapProgress(progress), ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(string filePath, StreamInfo streamInfoOverride, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertFileInternalAsync(filePath, streamInfoOverride, progress: null, ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(string filePath, StreamInfo streamInfoOverride, IProgress<ConversionProgress>? progress, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertFileInternalAsync(filePath, streamInfoOverride, progress, ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(string filePath, StreamInfo streamInfoOverride, Action<ConversionProgress> progress, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertFileInternalAsync(filePath, streamInfoOverride, WrapProgress(progress), ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(string filePath, ConversionRequest request, CancellationToken cancellationToken = default)
        => ConvertFileInternalAsync(filePath, null, progress: null, request ?? ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(string filePath, IProgress<ConversionProgress>? progress, ConversionRequest request, CancellationToken cancellationToken = default)
        => ConvertFileInternalAsync(filePath, null, progress, request ?? ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(string filePath, Action<ConversionProgress> progress, ConversionRequest request, CancellationToken cancellationToken = default)
        => ConvertFileInternalAsync(filePath, null, WrapProgress(progress), request ?? ConversionRequest.Default, cancellationToken);

    /// <summary>
    /// Convert a stream to Markdown.
    /// </summary>
    /// <param name="stream">The stream to convert.</param>
    /// <param name="streamInfo">Metadata about the stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The converted Markdown content.</returns>
    public Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        => ConvertStreamInternalAsync(stream, streamInfo, progress: null, ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken = default)
        => ConvertStreamInternalAsync(stream, streamInfo, progress, ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, Action<ConversionProgress> progress, CancellationToken cancellationToken = default)
        => ConvertStreamInternalAsync(stream, streamInfo, WrapProgress(progress), ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertStreamInternalAsync(stream, streamInfo, progress: null, ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, IProgress<ConversionProgress>? progress, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertStreamInternalAsync(stream, streamInfo, progress, ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, Action<ConversionProgress> progress, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertStreamInternalAsync(stream, streamInfo, WrapProgress(progress), ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, ConversionRequest request, CancellationToken cancellationToken = default)
        => ConvertStreamInternalAsync(stream, streamInfo, progress: null, request ?? ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken = default)
        => ConvertStreamInternalAsync(stream, streamInfo, progress, request ?? ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, ConversionRequest request, Action<ConversionProgress> progress, CancellationToken cancellationToken = default)
        => ConvertStreamInternalAsync(stream, streamInfo, WrapProgress(progress), request ?? ConversionRequest.Default, cancellationToken);

    private static IProgress<ConversionProgress>? WrapProgress(Action<ConversionProgress>? callback)
        => callback is null ? null : new Progress<ConversionProgress>(callback);

    private async Task<DocumentConverterResult> ConvertStreamInternalAsync(Stream stream, StreamInfo streamInfo, IProgress<ConversionProgress>? progress, ConversionRequest request, CancellationToken cancellationToken)
    {
        request ??= ConversionRequest.Default;

        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var activity = StartActivity(MarkItDownDiagnostics.ActivityNameConvertStream, streamInfo);
        var source = DescribeSource(streamInfo);

        var sw = Stopwatch.StartNew();
        var exceptions = new List<Exception>();

        var pipelineOptions = request.Pipeline;
        await using var buffer = await StreamPipelineBuffer.CreateAsync(stream, pipelineOptions.BufferSegmentSize, progress, _progressDetail, cancellationToken).ConfigureAwait(false);

        using var detectionStream = buffer.CreateStream();
        var guesses = BuildStreamInfoCandidates(detectionStream, streamInfo).ToList();
        activity?.SetTag(MarkItDownDiagnostics.ActivityTagNames.GuessCount, guesses.Count);
        _logger?.LogInformation("Converting {Source} with {GuessCount} candidate formats", source, guesses.Count);

        var totalAttempts = Math.Max(guesses.Count * _converters.Count, 1);
        var attempts = 0;

        void ReportProgress(string stage, int completed, string? details = null, bool detailedOnly = false, int? totalOverride = null)
        {
            if (progress is null)
            {
                return;
            }

            if (detailedOnly && _progressDetail != ProgressDetailLevel.Detailed)
            {
                return;
            }

            var totalForStage = totalOverride ?? totalAttempts;
            progress.Report(new ConversionProgress(stage, Math.Clamp(completed, 0, totalForStage), totalForStage, details));
        }

        ReportProgress("detect-formats", 0, $"{guesses.Count} candidate formats");

        var intelligence = CreateIntelligenceContext(request);
        var storageOptions = _options.ArtifactStorage ?? ArtifactStorageOptions.Default;
        using var contextScope = ConversionContextAccessor.Push(new ConversionContext(streamInfo, request, intelligence, _options.Segments, pipelineOptions, storageOptions, progress, _progressDetail));

        async Task<(DocumentConverterResult? Result, string? ConverterName, List<Exception> Errors)> TryConvertGroupInParallelAsync(StreamInfo currentGuess, IReadOnlyList<ConverterRegistration> group)
        {
            var errors = new ConcurrentBag<Exception>();
            var resultSource = new TaskCompletionSource<(DocumentConverterResult Result, string ConverterName)>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                await Parallel.ForEachAsync(group, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, pipelineOptions.MaxParallelConverterTasks),
                    CancellationToken = cts.Token
                }, async (registration, token) =>
                {
                    var converterName = registration.Converter.GetType().Name;
                    var converterDetails = $"{converterName} ({currentGuess.MimeType ?? currentGuess.Extension ?? "unknown"})";
                    var attemptIndex = Interlocked.Increment(ref attempts);
                    ReportProgress("trying-converter", attemptIndex - 1, converterDetails, detailedOnly: true);

                    try
                    {
                        using var acceptanceStream = buffer.CreateStream();
                        if (!registration.Converter.Accepts(acceptanceStream, currentGuess, token))
                        {
                            return;
                        }

                        using var conversionStream = buffer.CreateStream();
                        var result = await registration.Converter.ConvertAsync(conversionStream, currentGuess, token).ConfigureAwait(false);
                        if (resultSource.TrySetResult((result, converterName)))
                        {
                            cts.Cancel();
                        }
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex)
                    {
                        RecordFailure(converterName, currentGuess);
                        ReportProgress("converter-failed", Math.Min(attemptIndex, totalAttempts), $"{converterName}: {ex.GetType().Name}");
                        _logger?.LogWarning(ex, "Converter {ConverterType} failed for {Source}", converterName, source);
                        errors.Add(ex);
                    }
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Expected when a converter succeeds.
            }

            if (resultSource.Task.IsCompletedSuccessfully)
            {
                var (result, converterName) = await resultSource.Task.ConfigureAwait(false);
                return (result, converterName, errors.ToList());
            }

            return (null, null, errors.ToList());
        }

        var groupedConverters = pipelineOptions.EnableParallelConverterEvaluation
            ? _converters.GroupBy(r => r.Priority).OrderBy(g => g.Key).Select(g => g.ToList()).ToList()
            : null;

        foreach (var guess in guesses)
        {
            if (pipelineOptions.EnableParallelConverterEvaluation && groupedConverters is not null)
            {
                foreach (var group in groupedConverters)
                {
                    var (parallelResult, winnerName, parallelErrors) = await TryConvertGroupInParallelAsync(guess, group).ConfigureAwait(false);
                    if (parallelErrors.Count > 0)
                    {
                        exceptions.AddRange(parallelErrors);
                    }

                    if (parallelResult is not null && winnerName is not null)
                    {
                        attempts = totalAttempts;
                        RecordSuccess(guess);
                        ReportProgress("converter-selected", attempts, $"{winnerName} ({guess.MimeType ?? guess.Extension ?? "unknown"})", detailedOnly: false, totalOverride: totalAttempts);
                        activity?.SetTag(MarkItDownDiagnostics.ActivityTagNames.Converter, winnerName);
                        activity?.SetTag(MarkItDownDiagnostics.ActivityTagNames.DetectedMime, guess.MimeType);
                        activity?.SetTag(MarkItDownDiagnostics.ActivityTagNames.DetectedExtension, guess.Extension);
                        activity?.SetStatus(ActivityStatusCode.Ok);
                        _logger?.LogInformation("Converted {Source} using {ConverterType}", source, winnerName);

                        sw.Stop();
                        var usage = AggregateAiUsage(parallelResult);
                        var metadata = BuildConversionMetadata(sw.Elapsed, usage);
                        ReportProgress("completed", attempts, metadata.TryGetValue("converter.durationMs", out var duration) ? $"durationMs={duration}" : null, detailedOnly: false, totalOverride: totalAttempts);
                        return parallelResult.WithMetadata(metadata);
                    }
                }

                continue;
            }

            foreach (var registration in _converters)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var converterName = registration.Converter.GetType().Name;
                var converterDetails = $"{converterName} ({guess.MimeType ?? guess.Extension ?? "unknown"})";
                ReportProgress("trying-converter", attempts, converterDetails, detailedOnly: true);

                try
                {
                    using var acceptanceStream = buffer.CreateStream();
                    if (!registration.Converter.Accepts(acceptanceStream, guess, cancellationToken))
                    {
                        attempts++;
                        continue;
                    }

                    _logger?.LogDebug("Using converter {ConverterType} for {MimeType} {Extension}",
                        converterName,
                        guess.MimeType,
                        guess.Extension);

                    activity?.SetTag(MarkItDownDiagnostics.ActivityTagNames.Converter, converterName);
                    activity?.SetTag(MarkItDownDiagnostics.ActivityTagNames.DetectedMime, guess.MimeType);
                    activity?.SetTag(MarkItDownDiagnostics.ActivityTagNames.DetectedExtension, guess.Extension);

                    using var conversionStream = buffer.CreateStream();
                    var result = await registration.Converter.ConvertAsync(conversionStream, guess, cancellationToken).ConfigureAwait(false);
                    RecordSuccess(guess);
                    attempts = totalAttempts;
                    ReportProgress("converter-selected", attempts, $"{converterName} ({guess.MimeType ?? guess.Extension ?? "unknown"})", detailedOnly: false, totalOverride: totalAttempts);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    _logger?.LogInformation("Converted {Source} using {ConverterType}", source, converterName);

                    sw.Stop();
                    var usage = AggregateAiUsage(result);
                    var metadata = BuildConversionMetadata(sw.Elapsed, usage);
                    ReportProgress("completed", attempts, metadata.TryGetValue("converter.durationMs", out var duration) ? $"durationMs={duration}" : null, detailedOnly: false, totalOverride: totalAttempts);
                    return result.WithMetadata(metadata);
                }
                catch (Exception ex)
                {
                    attempts++;
                    ReportProgress("converter-failed", attempts, $"{converterDetails}: {ex.GetType().Name}", detailedOnly: true);
                    RecordFailure(converterName, guess);
                    _logger?.LogWarning(ex, "Converter {ConverterType} failed for {Source}", converterName, source);
                    exceptions.Add(ex);
                }
            }
        }

        var message = $"No converter available for file type. MimeType: {streamInfo.MimeType}, Extension: {streamInfo.Extension}";
        ReportProgress("failed", attempts, message);
        activity?.SetStatus(ActivityStatusCode.Error, message);
        _logger?.LogWarning("No converter could handle {Source} (MimeType: {MimeType}, Extension: {Extension})", source, streamInfo.MimeType, streamInfo.Extension);

        if (exceptions.Count > 0)
        {
            throw new UnsupportedFormatException(message, new AggregateException(exceptions));
        }

        throw new UnsupportedFormatException(message);
    }

    /// <summary>
    /// Convert content from a URL to Markdown.
    /// </summary>
    /// <param name="url">The URL to download and convert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The converted Markdown content.</returns>
    public Task<DocumentConverterResult> ConvertFromUrlAsync(string url, StreamInfo? streamInfoOverride = null, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(NormalizeHttpUri(url, nameof(url)), streamInfoOverride, progress: null, ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(string url, StreamInfo? streamInfoOverride, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(NormalizeHttpUri(url, nameof(url)), streamInfoOverride, progress, ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(string url, StreamInfo? streamInfoOverride, Action<ConversionProgress> progress, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(NormalizeHttpUri(url, nameof(url)), streamInfoOverride, WrapProgress(progress), ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(string url, StreamInfo? streamInfoOverride, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(NormalizeHttpUri(url, nameof(url)), streamInfoOverride, progress: null, ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(string url, StreamInfo? streamInfoOverride, IProgress<ConversionProgress>? progress, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(NormalizeHttpUri(url, nameof(url)), streamInfoOverride, progress, ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(string url, StreamInfo? streamInfoOverride, Action<ConversionProgress> progress, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(NormalizeHttpUri(url, nameof(url)), streamInfoOverride, WrapProgress(progress), ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(string url, StreamInfo? streamInfoOverride, ConversionRequest request, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(NormalizeHttpUri(url, nameof(url)), streamInfoOverride, progress: null, request ?? ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(string url, StreamInfo? streamInfoOverride, IProgress<ConversionProgress>? progress, ConversionRequest request, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(NormalizeHttpUri(url, nameof(url)), streamInfoOverride, progress, request ?? ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(string url, StreamInfo? streamInfoOverride, Action<ConversionProgress> progress, ConversionRequest request, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(NormalizeHttpUri(url, nameof(url)), streamInfoOverride, WrapProgress(progress), request ?? ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(Uri url, StreamInfo? streamInfoOverride = null, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(EnsureHttpUri(url, nameof(url)), streamInfoOverride, progress: null, ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(Uri url, StreamInfo? streamInfoOverride, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(EnsureHttpUri(url, nameof(url)), streamInfoOverride, progress, ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(Uri url, StreamInfo? streamInfoOverride, Action<ConversionProgress> progress, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(EnsureHttpUri(url, nameof(url)), streamInfoOverride, WrapProgress(progress), ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(Uri url, StreamInfo? streamInfoOverride, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(EnsureHttpUri(url, nameof(url)), streamInfoOverride, progress: null, ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(Uri url, StreamInfo? streamInfoOverride, IProgress<ConversionProgress>? progress, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(EnsureHttpUri(url, nameof(url)), streamInfoOverride, progress, ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(Uri url, StreamInfo? streamInfoOverride, Action<ConversionProgress> progress, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(EnsureHttpUri(url, nameof(url)), streamInfoOverride, WrapProgress(progress), ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(Uri url, StreamInfo? streamInfoOverride, ConversionRequest request, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(EnsureHttpUri(url, nameof(url)), streamInfoOverride, progress: null, request ?? ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(Uri url, StreamInfo? streamInfoOverride, IProgress<ConversionProgress>? progress, ConversionRequest request, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(EnsureHttpUri(url, nameof(url)), streamInfoOverride, progress, request ?? ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertFromUrlAsync(Uri url, StreamInfo? streamInfoOverride, Action<ConversionProgress> progress, ConversionRequest request, CancellationToken cancellationToken = default)
        => ConvertFromUrlInternalAsync(EnsureHttpUri(url, nameof(url)), streamInfoOverride, WrapProgress(progress), request ?? ConversionRequest.Default, cancellationToken);

    private async Task<DocumentConverterResult> ConvertFromUrlInternalAsync(Uri url, StreamInfo? streamInfoOverride, IProgress<ConversionProgress>? progress, ConversionRequest request, CancellationToken cancellationToken)
    {
        if (_httpClient is null)
            throw new InvalidOperationException("HTTP client is required for URL conversion. Provide one in the constructor.");

        using var activity = StartActivity(MarkItDownDiagnostics.ActivityNameConvertUrl);
        var urlText = url.ToString();
        activity?.SetTag(MarkItDownDiagnostics.ActivityTagNames.Url, urlText);

        Activity? downloadActivity = null;
        try
        {
            _logger?.LogInformation("Downloading {Url}", urlText);
            downloadActivity = StartActivity(MarkItDownDiagnostics.ActivityNameDownload);
            downloadActivity?.SetTag("http.url", urlText);

            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            downloadActivity?.SetTag("http.status_code", (int)response.StatusCode);
            activity?.SetTag("http.status_code", (int)response.StatusCode);
            response.EnsureSuccessStatusCode();

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var streamInfo = CreateStreamInfoFromUrl(url, response);
            if (streamInfoOverride is not null)
            {
                streamInfo = streamInfo.CopyWith(streamInfoOverride);
            }

            if (!string.IsNullOrWhiteSpace(streamInfo.FileName))
            {
                activity?.SetTag(MarkItDownDiagnostics.ActivityTagNames.FileName, streamInfo.FileName);
            }

            if (!string.IsNullOrWhiteSpace(streamInfo.MimeType))
            {
                activity?.SetTag(MarkItDownDiagnostics.ActivityTagNames.Mime, streamInfo.MimeType);
            }

            if (!string.IsNullOrWhiteSpace(streamInfo.Extension))
            {
                activity?.SetTag(MarkItDownDiagnostics.ActivityTagNames.Extension, streamInfo.Extension);
            }

            activity?.SetTag("content.length", response.Content.Headers.ContentLength ?? 0);

            await using var downloadHandle = await DiskBufferHandle.FromStreamAsync(
                contentStream,
                streamInfo.Extension,
                bufferSize: 1024 * 128,
                onChunkWritten: null,
                cancellationToken).ConfigureAwait(false);

            downloadActivity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetStatus(ActivityStatusCode.Ok);

            var overrides = streamInfo.CopyWith(localPath: downloadHandle.FilePath);
            return await ConvertFileInternalAsync(downloadHandle.FilePath, overrides, progress, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            downloadActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger?.LogError(ex, "Failed to download or convert {Url}", url);
            throw;
        }
        finally
        {
            downloadActivity?.Dispose();
        }
    }

    /// <summary>
    /// Convert a generic URI (file, data, http, https) to Markdown.
    /// </summary>
    public Task<DocumentConverterResult> ConvertUriAsync(string uri, StreamInfo? streamInfo = null, CancellationToken cancellationToken = default)
        => ConvertUriInternalAsync(uri, streamInfo, progress: null, ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertUriAsync(string uri, StreamInfo? streamInfo, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken = default)
        => ConvertUriInternalAsync(uri, streamInfo, progress, ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertUriAsync(string uri, StreamInfo? streamInfo, Action<ConversionProgress> progress, CancellationToken cancellationToken = default)
        => ConvertUriInternalAsync(uri, streamInfo, WrapProgress(progress), ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertUriAsync(string uri, StreamInfo? streamInfo, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertUriInternalAsync(uri, streamInfo, progress: null, ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertUriAsync(string uri, StreamInfo? streamInfo, IProgress<ConversionProgress>? progress, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertUriInternalAsync(uri, streamInfo, progress, ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertUriAsync(string uri, StreamInfo? streamInfo, Action<ConversionProgress> progress, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default)
        => ConvertUriInternalAsync(uri, streamInfo, WrapProgress(progress), ConversionRequest.FromConfiguration(configure), cancellationToken);

    public Task<DocumentConverterResult> ConvertUriAsync(string uri, StreamInfo? streamInfo, ConversionRequest request, CancellationToken cancellationToken = default)
        => ConvertUriInternalAsync(uri, streamInfo, progress: null, request ?? ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertUriAsync(string uri, StreamInfo? streamInfo, IProgress<ConversionProgress>? progress, ConversionRequest request, CancellationToken cancellationToken = default)
        => ConvertUriInternalAsync(uri, streamInfo, progress, request ?? ConversionRequest.Default, cancellationToken);

    public Task<DocumentConverterResult> ConvertUriAsync(string uri, StreamInfo? streamInfo, Action<ConversionProgress> progress, ConversionRequest request, CancellationToken cancellationToken = default)
        => ConvertUriInternalAsync(uri, streamInfo, WrapProgress(progress), request ?? ConversionRequest.Default, cancellationToken);

    private async Task<DocumentConverterResult> ConvertUriInternalAsync(string uri, StreamInfo? streamInfo, IProgress<ConversionProgress>? progress, ConversionRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(uri);

        var trimmed = uri.Trim();

        if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var localPath = UriUtilities.ResolveFilePath(trimmed);
            return await ConvertFileInternalAsync(localPath, streamInfo, progress, request, cancellationToken).ConfigureAwait(false);
        }

        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var payload = UriUtilities.ParseDataUri(trimmed);
            string? normalizedExtension = null;
            if (!string.IsNullOrWhiteSpace(payload.MimeType) &&
                MimeHelper.TryGetExtensions(payload.MimeType, out var extensions) &&
                extensions.Count > 0)
            {
                var ext = extensions.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(ext))
                {
                    normalizedExtension = ext.StartsWith('.') ? ext : "." + ext;
                }
            }
            var baseInfo = new StreamInfo(
                mimeType: payload.MimeType,
                extension: normalizedExtension,
                charset: TryGetEncoding(payload.Charset));

            if (streamInfo is not null)
            {
                baseInfo = baseInfo.CopyWith(streamInfo);
            }

            await using var dataHandle = await DiskBufferHandle.FromBytesAsync(payload.Data, normalizedExtension, cancellationToken).ConfigureAwait(false);
            var overrides = baseInfo.CopyWith(localPath: dataHandle.FilePath);
            return await ConvertFileInternalAsync(dataHandle.FilePath, overrides, progress, request, cancellationToken).ConfigureAwait(false);
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var httpUri = NormalizeHttpUri(trimmed, nameof(uri));
            return await ConvertFromUrlInternalAsync(httpUri, streamInfo, progress, request, cancellationToken).ConfigureAwait(false);
        }

        throw new ArgumentException($"Unsupported URI scheme for '{uri}'.", nameof(uri));
    }

    private void RegisterBuiltInConverters()
    {
        foreach (var converter in CreateBuiltInConverters())
        {
            RegisterConverter(converter);
        }
    }

    private IntelligenceProviderHub CreateIntelligenceContext(ConversionRequest request)
    {
        var document = ResolveDocumentProvider(request.Intelligence.Document);
        var image = ResolveImageProvider(request.Intelligence.Image);
        var media = ResolveMediaProvider(request.Intelligence.Media);

        return new IntelligenceProviderHub(document, image, media, _intelligenceProviders.AiModels);
    }

    private IDocumentIntelligenceProvider? ResolveDocumentProvider(DocumentIntelligenceRequest? request)
    {
        if (request?.PreferredProvider is null)
        {
            return _intelligenceProviders.Document;
        }

        return request.PreferredProvider.Value switch
        {
            DocumentIntelligenceProviderKind.Azure => BuildAzureDocumentProvider(request.Azure) ?? _intelligenceProviders.Document,
            DocumentIntelligenceProviderKind.Google => BuildGoogleDocumentProvider(request.Google) ?? _intelligenceProviders.Document,
            DocumentIntelligenceProviderKind.Aws => BuildAwsDocumentProvider(request.Aws) ?? _intelligenceProviders.Document,
            DocumentIntelligenceProviderKind.Custom => _options.DocumentIntelligenceProvider ?? _intelligenceProviders.Document,
            _ => _intelligenceProviders.Document
        };
    }

    private IDocumentIntelligenceProvider? BuildAzureDocumentProvider(AzureDocumentIntelligenceOverrides? overrides)
    {
        var baseOptions = _options.AzureIntelligence?.DocumentIntelligence;
        var endpoint = overrides?.Endpoint ?? baseOptions?.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        var options = new AzureDocumentIntelligenceOptions
        {
            Endpoint = endpoint,
            ApiKey = overrides?.ApiKey ?? baseOptions?.ApiKey,
            ModelId = overrides?.ModelId ?? baseOptions?.ModelId ?? "prebuilt-layout"
        };

        return new AzureDocumentIntelligenceProvider(options);
    }

    private IDocumentIntelligenceProvider? BuildGoogleDocumentProvider(GoogleDocumentIntelligenceOverrides? overrides)
    {
        var baseOptions = _options.GoogleIntelligence?.DocumentIntelligence;
        var projectId = overrides?.ProjectId ?? baseOptions?.ProjectId;
        var location = overrides?.Location ?? baseOptions?.Location;
        var processorId = overrides?.ProcessorId ?? baseOptions?.ProcessorId;

        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(processorId))
        {
            return null;
        }

        var options = new GoogleDocumentIntelligenceOptions
        {
            ProjectId = projectId,
            Location = location,
            ProcessorId = processorId,
            Credential = baseOptions?.Credential,
            JsonCredentials = baseOptions?.JsonCredentials,
            CredentialsPath = baseOptions?.CredentialsPath
        };

        return new GoogleDocumentIntelligenceProvider(options);
    }

    private IDocumentIntelligenceProvider? BuildAwsDocumentProvider(AwsDocumentIntelligenceOverrides? overrides)
    {
        var baseOptions = _options.AwsIntelligence?.DocumentIntelligence;
        if (baseOptions is null && overrides is null)
        {
            return _intelligenceProviders.Document;
        }

        var options = new AwsDocumentIntelligenceOptions
        {
            Credentials = baseOptions?.Credentials,
            AccessKeyId = baseOptions?.AccessKeyId,
            SecretAccessKey = baseOptions?.SecretAccessKey,
            SessionToken = baseOptions?.SessionToken,
            Region = overrides?.Region ?? baseOptions?.Region
        };

        return new AwsDocumentIntelligenceProvider(options);
    }

    private IImageUnderstandingProvider? ResolveImageProvider(ImageUnderstandingRequest? request)
    {
        if (request?.PreferredProvider is null)
        {
            return _intelligenceProviders.Image;
        }

        return request.PreferredProvider.Value switch
        {
            ImageUnderstandingProviderKind.Azure => BuildAzureImageProvider() ?? _intelligenceProviders.Image,
            ImageUnderstandingProviderKind.Google => BuildGoogleImageProvider() ?? _intelligenceProviders.Image,
            ImageUnderstandingProviderKind.Aws => BuildAwsImageProvider() ?? _intelligenceProviders.Image,
            ImageUnderstandingProviderKind.Custom => _options.ImageUnderstandingProvider ?? _intelligenceProviders.Image,
            _ => _intelligenceProviders.Image
        };
    }

    private IImageUnderstandingProvider? BuildAzureImageProvider()
    {
        var options = _options.AzureIntelligence?.Vision;
        return options is null ? null : new AzureImageUnderstandingProvider(options);
    }

    private IImageUnderstandingProvider? BuildGoogleImageProvider()
    {
        var options = _options.GoogleIntelligence?.Vision;
        return options is null ? null : new GoogleImageUnderstandingProvider(options);
    }

    private IImageUnderstandingProvider? BuildAwsImageProvider()
    {
        var options = _options.AwsIntelligence?.Vision;
        return options is null ? null : new AwsImageUnderstandingProvider(options);
    }

    private IMediaTranscriptionProvider? ResolveMediaProvider(MediaTranscriptionRequest? request)
    {
        if (request?.PreferredProvider is null)
        {
            return _intelligenceProviders.Media;
        }

        return request.PreferredProvider.Value switch
        {
            MediaTranscriptionProviderKind.Azure => BuildAzureMediaProvider() ?? _intelligenceProviders.Media,
            MediaTranscriptionProviderKind.Google => BuildGoogleMediaProvider() ?? _intelligenceProviders.Media,
            MediaTranscriptionProviderKind.Aws => BuildAwsMediaProvider() ?? _intelligenceProviders.Media,
            MediaTranscriptionProviderKind.Custom => _options.MediaTranscriptionProvider ?? _intelligenceProviders.Media,
            _ => _intelligenceProviders.Media
        };
    }

    private IMediaTranscriptionProvider? BuildAzureMediaProvider()
    {
        var options = _options.AzureIntelligence?.Media;
        return options is null ? null : new AzureMediaTranscriptionProvider(options);
    }

    private IMediaTranscriptionProvider? BuildGoogleMediaProvider()
    {
        var options = _options.GoogleIntelligence?.Media;
        return options is null ? null : new GoogleMediaTranscriptionProvider(options);
    }

    private IMediaTranscriptionProvider? BuildAwsMediaProvider()
    {
        var options = _options.AwsIntelligence?.Media;
        return options is null ? null : new AwsMediaTranscriptionProvider(options);
    }


    private IConversionPipeline BuildConversionPipeline()
    {
        var middleware = new List<IConversionMiddleware>();

        var aiImageEnrichmentEnabled = _options.EnableAiImageEnrichment && _options.Segments.Image.EnableAiEnrichment;
        if (aiImageEnrichmentEnabled)
        {
            middleware.Add(new AiImageEnrichmentMiddleware());
        }

        if (_options.ConversionMiddleware is { Count: > 0 })
        {
            middleware.AddRange(_options.ConversionMiddleware);
        }

        middleware.Add(new SegmentProgressMiddleware());

        return new ConversionPipeline(middleware, _intelligenceProviders.AiModels ?? NullAiModelProvider.Instance, _logger, _options.Segments, _progressDetail);
    }

    private IntelligenceProviderHub InitializeIntelligenceProviders()
    {
        var imageOptions = _options.Segments.Image;

        IDocumentIntelligenceProvider? documentProvider = _options.DocumentIntelligenceProvider;
        if (documentProvider is null)
        {
            if (_options.AzureIntelligence?.DocumentIntelligence is { } azureDoc)
            {
                documentProvider = new AzureDocumentIntelligenceProvider(azureDoc);
            }
            else if (_options.GoogleIntelligence?.DocumentIntelligence is { } googleDoc)
            {
                documentProvider = new GoogleDocumentIntelligenceProvider(googleDoc);
            }
            else if (_options.AwsIntelligence?.DocumentIntelligence is { } awsDoc)
            {
                documentProvider = new AwsDocumentIntelligenceProvider(awsDoc);
            }
            else if (_options.DocumentIntelligence is { } legacy)
            {
                documentProvider = CreateLegacyDocumentIntelligenceProvider(legacy);
            }
        }

        if (!imageOptions.EnableDocumentIntelligence)
        {
            documentProvider = null;
        }

        IImageUnderstandingProvider? imageProvider = _options.ImageUnderstandingProvider;
        if (imageProvider is null)
        {
            if (_options.AzureIntelligence?.Vision is { } azureVision)
            {
                imageProvider = new AzureImageUnderstandingProvider(azureVision);
            }
            else if (_options.GoogleIntelligence?.Vision is { } googleVision)
            {
                imageProvider = new GoogleImageUnderstandingProvider(googleVision);
            }
            else if (_options.AwsIntelligence?.Vision is { } awsVision)
            {
                imageProvider = new AwsImageUnderstandingProvider(awsVision);
            }
        }

        if (!imageOptions.EnableImageUnderstandingProvider)
        {
            imageProvider = null;
        }

        IMediaTranscriptionProvider? mediaProvider = _options.MediaTranscriptionProvider;
        if (mediaProvider is null)
        {
            if (_options.AzureIntelligence?.Media is { } azureMedia)
            {
                mediaProvider = new AzureMediaTranscriptionProvider(azureMedia);
            }
            else if (_options.GoogleIntelligence?.Media is { } googleMedia)
            {
                mediaProvider = new GoogleMediaTranscriptionProvider(googleMedia);
            }
            else if (_options.AwsIntelligence?.Media is { } awsMedia)
            {
                mediaProvider = new AwsMediaTranscriptionProvider(awsMedia);
            }
        }

        var aiModels = _options.AiModels ?? NullAiModelProvider.Instance;

        return new IntelligenceProviderHub(documentProvider, imageProvider, mediaProvider, aiModels);
    }

    private static IDocumentIntelligenceProvider? CreateLegacyDocumentIntelligenceProvider(DocumentIntelligenceOptions legacy)
    {
        if (string.IsNullOrWhiteSpace(legacy.Endpoint))
        {
            return null;
        }

        AzureDocumentIntelligenceOptions azureOptions = new()
        {
            Endpoint = legacy.Endpoint,
            ApiKey = legacy.Credential as string
        };

        DocumentAnalysisClient? client = null;

        if (legacy.Credential is AzureKeyCredential keyCredential)
        {
            client = new DocumentAnalysisClient(new Uri(legacy.Endpoint), keyCredential);
        }
        else if (legacy.Credential is TokenCredential tokenCredential)
        {
            DocumentAnalysisClientOptions? options = null;
            if (!string.IsNullOrWhiteSpace(legacy.ApiVersion) && Enum.TryParse<DocumentAnalysisClientOptions.ServiceVersion>(legacy.ApiVersion, ignoreCase: true, out var parsedVersion))
            {
                options = new DocumentAnalysisClientOptions(parsedVersion);
            }

            client = options is null
                ? new DocumentAnalysisClient(new Uri(legacy.Endpoint), tokenCredential)
                : new DocumentAnalysisClient(new Uri(legacy.Endpoint), tokenCredential, options);
        }
        else if (legacy.Credential is string keyString)
        {
            client = new DocumentAnalysisClient(new Uri(legacy.Endpoint), new AzureKeyCredential(keyString));
        }
        else if (legacy.Credential is null)
        {
            client = new DocumentAnalysisClient(new Uri(legacy.Endpoint), new DefaultAzureCredential());
        }

        return client is not null
            ? new AzureDocumentIntelligenceProvider(azureOptions, client)
            : new AzureDocumentIntelligenceProvider(azureOptions);
    }

    private IEnumerable<DocumentConverterBase> CreateBuiltInConverters()
    {
        DocumentConverterBase CreateImageConverter() => new ImageConverter(_options.ExifToolPath, _options.ImageCaptioner);
        DocumentConverterBase CreateAudioConverter() => new AudioConverter(_options.ExifToolPath, _options.AudioTranscriber, _options.Segments, _intelligenceProviders.Media);

        var converters = new List<DocumentConverterBase>
        {
            new YouTubeUrlConverter(_youTubeMetadataProvider),
            new HtmlConverter(),
            new WikipediaConverter(),
            new BingSerpConverter(),
            new RssFeedConverter(),
            new JsonConverter(),
            new MetaMdConverter(),
            new DocBookConverter(),
            new JatsConverter(),
            new OpmlConverter(),
            new Fb2Converter(),
            new EndNoteXmlConverter(),
            new BibTexConverter(),
            new RisConverter(),
            new CslJsonConverter(),
            new OdtConverter(),
            new RtfConverter(),
            new LatexConverter(),
            new RstConverter(),
            new AsciiDocConverter(),
            new OrgConverter(),
            new DjotConverter(),
            new TypstConverter(),
            new TextileConverter(),
            new WikiMarkupConverter(),
            new MermaidConverter(),
            new GraphvizConverter(),
            new PlantUmlConverter(),
            new TikzConverter(),
            new JupyterNotebookConverter(),
            new CsvConverter(),
            new EpubConverter(),
            new EmlConverter(),
            new XmlConverter(),
            new PdfConverter(_options.Segments, _intelligenceProviders.Document, _intelligenceProviders.Image, _conversionPipeline),
            new DocxConverter(_options.Segments, _conversionPipeline, _intelligenceProviders.Image, _intelligenceProviders.Document, _intelligenceProviders.AiModels),
            new XlsxConverter(_options.Segments),
            new PptxConverter(_options.Segments, _conversionPipeline, _intelligenceProviders.Image),
            new ZipConverter(CreateZipInnerConverters(CreateImageConverter, CreateAudioConverter)),
            CreateAudioConverter(),
            CreateImageConverter(),
            new PlainTextConverter(),
        };

        return converters;
    }

    private IEnumerable<DocumentConverterBase> CreateZipInnerConverters(Func<DocumentConverterBase> imageConverterFactory, Func<DocumentConverterBase> audioConverterFactory)
    {
        return new DocumentConverterBase[]
        {
            new YouTubeUrlConverter(_youTubeMetadataProvider),
            new HtmlConverter(),
            new WikipediaConverter(),
            new BingSerpConverter(),
            new RssFeedConverter(),
            new JsonConverter(),
            new MetaMdConverter(),
            new DocBookConverter(),
            new JatsConverter(),
            new OpmlConverter(),
            new Fb2Converter(),
            new EndNoteXmlConverter(),
            new BibTexConverter(),
            new RisConverter(),
            new CslJsonConverter(),
            new OdtConverter(),
            new RtfConverter(),
            new LatexConverter(),
            new RstConverter(),
            new AsciiDocConverter(),
            new OrgConverter(),
            new DjotConverter(),
            new TypstConverter(),
            new TextileConverter(),
            new WikiMarkupConverter(),
            new MermaidConverter(),
            new GraphvizConverter(),
            new PlantUmlConverter(),
            new TikzConverter(),
            new JupyterNotebookConverter(),
            new CsvConverter(),
            new EmlConverter(),
            new XmlConverter(),
            new PdfConverter(_options.Segments, _intelligenceProviders.Document, _intelligenceProviders.Image, _conversionPipeline),
            new DocxConverter(_options.Segments, _conversionPipeline, _intelligenceProviders.Image, _intelligenceProviders.Document, _intelligenceProviders.AiModels),
            new XlsxConverter(_options.Segments),
            new PptxConverter(_options.Segments, _conversionPipeline, _intelligenceProviders.Image),
            audioConverterFactory(),
            imageConverterFactory(),
            new PlainTextConverter(),
        };
    }

    private Activity? StartActivity(string name, StreamInfo? streamInfo = null)
    {
        if (!_options.EnableTelemetry)
        {
            return null;
        }

        var activity = _activitySource.StartActivity(name, ActivityKind.Internal);
        if (activity is not null && streamInfo is not null)
        {
            if (!string.IsNullOrWhiteSpace(streamInfo.MimeType))
            {
                activity.SetTag(MarkItDownDiagnostics.ActivityTagNames.Mime, streamInfo.MimeType);
            }

            if (!string.IsNullOrWhiteSpace(streamInfo.Extension))
            {
                activity.SetTag(MarkItDownDiagnostics.ActivityTagNames.Extension, streamInfo.Extension);
            }

            if (!string.IsNullOrWhiteSpace(streamInfo.FileName))
            {
                activity.SetTag(MarkItDownDiagnostics.ActivityTagNames.FileName, streamInfo.FileName);
            }

            if (!string.IsNullOrWhiteSpace(streamInfo.Url))
            {
                activity.SetTag(MarkItDownDiagnostics.ActivityTagNames.Url, streamInfo.Url);
            }
        }

        return activity;
    }

    private void RecordSuccess(StreamInfo guess)
    {
        if (_conversionCounter is null)
        {
            return;
        }

        _conversionCounter.Add(1,
            new KeyValuePair<string, object?>(MarkItDownDiagnostics.ActivityTagNames.Mime, guess.MimeType ?? string.Empty),
            new KeyValuePair<string, object?>(MarkItDownDiagnostics.ActivityTagNames.Extension, guess.Extension ?? string.Empty));
    }

    private void RecordFailure(string converterName, StreamInfo guess)
    {
        if (_conversionFailureCounter is null)
        {
            return;
        }

        _conversionFailureCounter.Add(1,
            new KeyValuePair<string, object?>(MarkItDownDiagnostics.ActivityTagNames.Converter, converterName),
            new KeyValuePair<string, object?>(MarkItDownDiagnostics.ActivityTagNames.Mime, guess.MimeType ?? string.Empty),
            new KeyValuePair<string, object?>(MarkItDownDiagnostics.ActivityTagNames.Extension, guess.Extension ?? string.Empty));
    }

    private static string DescribeSource(StreamInfo streamInfo)
    {
        if (!string.IsNullOrWhiteSpace(streamInfo.FileName))
        {
            return streamInfo.FileName!;
        }

        if (!string.IsNullOrWhiteSpace(streamInfo.LocalPath))
        {
            return streamInfo.LocalPath!;
        }

        if (!string.IsNullOrWhiteSpace(streamInfo.Url))
        {
            return streamInfo.Url!;
        }

        return "stream";
    }

    private async Task<DocumentConverterResult> ConvertFileInternalAsync(string filePath, StreamInfo? overrides, IProgress<ConversionProgress>? progress, ConversionRequest request, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        using var activity = StartActivity(MarkItDownDiagnostics.ActivityNameConvertFile);
        activity?.SetTag(MarkItDownDiagnostics.ActivityTagNames.Path, filePath);
        _logger?.LogInformation("Converting file {FilePath}", filePath);

        using var fileStream = File.OpenRead(filePath);
        var streamInfo = CreateStreamInfoFromFile(filePath);
        if (overrides is not null)
        {
            streamInfo = streamInfo.CopyWith(overrides);
        }

        try
        {
            var result = await ConvertStreamInternalAsync(fileStream, streamInfo, progress, request, cancellationToken).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private static StreamInfo CreateStreamInfoFromFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        var filename = Path.GetFileName(filePath);
        
        // Simple MIME type detection based on extension
        var mimeType = GetMimeTypeFromExtension(extension);
        
        return new StreamInfo(mimeType, extension, null, filename);
    }

    private static StreamInfo CreateStreamInfoFromUrl(Uri uri, HttpResponseMessage response)
    {
        var extension = Path.GetExtension(uri.LocalPath);
        var filename = Path.GetFileName(uri.LocalPath);
        
        // Try to get MIME type from response
        var mimeType = response.Content.Headers.ContentType?.MediaType ?? GetMimeTypeFromExtension(extension);
        
        // Try to get charset from response
        Encoding? charset = null;
        var charsetName = response.Content.Headers.ContentType?.CharSet;
        if (!string.IsNullOrEmpty(charsetName))
        {
            try
            {
                charset = Encoding.GetEncoding(charsetName);
            }
            catch
            {
                // Ignore invalid charset names
            }
        }
        
        return new StreamInfo(mimeType, extension, charset, filename, url: uri.ToString());
    }

    private static Uri NormalizeHttpUri(string url, string parameterName)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("URL must be an absolute URI.", parameterName);
        }

        return EnsureHttpUri(uri, parameterName);
    }

    private static Uri EnsureHttpUri(Uri url, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (!url.IsAbsoluteUri ||
            (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("URL must use the HTTP or HTTPS scheme.", parameterName);
        }

        return url;
    }

    private static IReadOnlyList<StreamInfo> BuildStreamInfoCandidates(Stream detectionStream, StreamInfo baseInfo)
    {
        var normalized = NormalizeStreamInfo(baseInfo);
        var candidates = new List<StreamInfo> { normalized };

        var detectedMime = DetectMimeByContent(detectionStream);
        if (!string.IsNullOrWhiteSpace(detectedMime) && !MimeEquals(detectedMime, normalized.MimeType))
        {
            var detectedExtension = normalized.Extension ?? GuessExtensionFromMime(detectedMime);
            var candidate = normalized.CopyWith(mimeType: detectedMime, extension: detectedExtension);

            if (!candidates.Any(c => MimeEquals(c.MimeType, candidate.MimeType) &&
                                     string.Equals(c.Extension, candidate.Extension, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    private static StreamInfo NormalizeStreamInfo(StreamInfo baseInfo)
    {
        var extension = NormalizeExtension(baseInfo.Extension)
            ?? NormalizeExtension(Path.GetExtension(baseInfo.FileName))
            ?? NormalizeExtension(Path.GetExtension(baseInfo.LocalPath));

        var candidate = baseInfo.CopyWith(extension: extension);
        var mime = candidate.ResolveMimeType();
        if (IsUnknownMime(mime))
        {
            mime = null;
        }

        extension ??= GuessExtensionFromMime(mime);

        return candidate.CopyWith(mimeType: mime, extension: extension);
    }

    private static string? DetectMimeByContent(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return null;
        }

        var original = stream.Position;
        try
        {
            stream.Position = 0;
            var detected = MimeHelper.GetMimeTypeByContent(stream);
            if (!IsUnknownMime(detected))
            {
                return detected;
            }

            stream.Position = 0;
            return TryDetectTextMime(stream, out var textMime)
                ? textMime
                : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = original;
            }
        }
    }

    private static bool TryDetectTextMime(Stream stream, out string? mime)
    {
        const int ProbeLength = 4096;
        mime = null;

        var buffer = ArrayPool<byte>.Shared.Rent(ProbeLength);
        try
        {
            var read = stream.Read(buffer, 0, ProbeLength);
            if (read == 0)
            {
                return false;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, read);
            var trimmed = text.AsSpan().TrimStart();
            if (trimmed.IsEmpty)
            {
                return false;
            }

            var first = trimmed[0];
            if (first == '{' || first == '[')
            {
                mime = MimeHelper.JSON ?? "application/json";
                return true;
            }

            if (trimmed.StartsWith("<?xml".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                var afterDeclaration = trimmed;
                var declarationEnd = trimmed.IndexOf("?>".AsSpan(), StringComparison.Ordinal);
                if (declarationEnd >= 0 && declarationEnd + 2 <= trimmed.Length)
                {
                    afterDeclaration = trimmed[(declarationEnd + 2)..].TrimStart();
                }

                if (afterDeclaration.StartsWith("<rss".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    mime = MimeHelper.RSS ?? "application/rss+xml";
                    return true;
                }

                if (afterDeclaration.StartsWith("<feed".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    mime = MimeHelper.ATOM ?? "application/atom+xml";
                    return true;
                }

                mime = MimeHelper.XML ?? "application/xml";
                return true;
            }

            if (trimmed.StartsWith("<rss".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                mime = MimeHelper.RSS ?? "application/rss+xml";
                return true;
            }

            if (trimmed.StartsWith("<feed".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                mime = MimeHelper.ATOM ?? "application/atom+xml";
                return true;
            }

            if (first == '<')
            {
                mime = MimeHelper.HTML ?? "text/html";
                return true;
            }

            if (LooksLikeDelimited(text, ','))
            {
                mime = MimeHelper.CSV ?? "text/csv";
                return true;
            }

            if (LooksLikeDelimited(text, '\t'))
            {
                mime = MimeHelper.GetMimeType(".tsv") ?? "text/tab-separated-values";
                return true;
            }

            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }
        }
    }

    private static bool LooksLikeDelimited(string sample, char separator)
    {
        if (string.IsNullOrWhiteSpace(sample))
        {
            return false;
        }

        var lines = sample.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
        {
            return false;
        }

        var counts = new List<int>();
        foreach (var line in lines)
        {
            var count = line.Count(c => c == separator);
            if (count == 0)
            {
                continue;
            }

            counts.Add(count);
            if (counts.Count >= 4)
            {
                break;
            }
        }

        if (counts.Count < 2)
        {
            return false;
        }

        var first = counts[0];
        if (first == 0)
        {
            return false;
        }

        return counts.All(count => count == first);
    }

    private static string? GuessExtensionFromMime(string? mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
        {
            return null;
        }

        if (!MimeHelper.TryGetExtensions(mime, out var extensions) || extensions.Count == 0)
        {
            return null;
        }

        var extension = extensions.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        return extension.StartsWith('.') ? extension : "." + extension;
    }

    private static string? NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        return extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
    }

    private static bool MimeEquals(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnknownMime(string? mime)
        => string.IsNullOrWhiteSpace(mime) || MimeEquals(mime, MimeHelper.BIN);

    private static string? GetMimeTypeFromExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var ext = extension.StartsWith('.') ? extension : "." + extension;
        var mime = MimeHelper.GetMimeType(ext);
        return string.IsNullOrWhiteSpace(mime) ? null : mime;
    }

    private static Encoding? TryGetEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return null;
        }

        try
        {
            return Encoding.GetEncoding(charset);
        }
        catch
        {
            return null;
        }
    }

    private static AiUsageSnapshot AggregateAiUsage(DocumentConverterResult result)
    {
        if (result is null)
        {
            return AiUsageSnapshot.Empty;
        }

        var usage = AiUsageSnapshot.Empty;

        var images = result.Artifacts?.Images;
        if (images is null || images.Count == 0)
        {
            return usage;
        }

        foreach (var image in images)
        {
            if (image?.Metadata is not { Count: > 0 })
            {
                continue;
            }

            usage += AiUsageSnapshot.FromMetadata(new ReadOnlyDictionary<string, string>(image.Metadata));
        }

        return usage;
    }

    private static IReadOnlyDictionary<string, string> BuildConversionMetadata(TimeSpan elapsed, AiUsageSnapshot usage)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MetadataKeys.ConverterDurationMs] = Math.Round(elapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)
        };

        if (!usage.IsEmpty)
        {
            metadata[MetadataKeys.AiTotalTokens] = usage.TotalTokens.ToString(CultureInfo.InvariantCulture);
            metadata[MetadataKeys.AiInputTokens] = usage.InputTokens.ToString(CultureInfo.InvariantCulture);
            metadata[MetadataKeys.AiOutputTokens] = usage.OutputTokens.ToString(CultureInfo.InvariantCulture);
            metadata[MetadataKeys.AiCallCount] = usage.CallCount.ToString(CultureInfo.InvariantCulture);
            if (usage.InputAudioTokens > 0)
            {
                metadata[MetadataKeys.AiInputAudioTokens] = usage.InputAudioTokens.ToString(CultureInfo.InvariantCulture);
            }

            if (usage.InputCachedTokens > 0)
            {
                metadata[MetadataKeys.AiInputCachedTokens] = usage.InputCachedTokens.ToString(CultureInfo.InvariantCulture);
            }

            if (usage.OutputAudioTokens > 0)
            {
                metadata[MetadataKeys.AiOutputAudioTokens] = usage.OutputAudioTokens.ToString(CultureInfo.InvariantCulture);
            }

            if (usage.OutputReasoningTokens > 0)
            {
                metadata[MetadataKeys.AiOutputReasoningTokens] = usage.OutputReasoningTokens.ToString(CultureInfo.InvariantCulture);
            }

            if (usage.OutputAcceptedPredictionTokens > 0)
            {
                metadata[MetadataKeys.AiOutputAcceptedPredictionTokens] = usage.OutputAcceptedPredictionTokens.ToString(CultureInfo.InvariantCulture);
            }

            if (usage.OutputRejectedPredictionTokens > 0)
            {
                metadata[MetadataKeys.AiOutputRejectedPredictionTokens] = usage.OutputRejectedPredictionTokens.ToString(CultureInfo.InvariantCulture);
            }

            if (usage.CostUsd > 0)
            {
                metadata[MetadataKeys.AiCostUsd] = usage.CostUsd.ToString(CultureInfo.InvariantCulture);
            }
        }

        return metadata;
    }
}
