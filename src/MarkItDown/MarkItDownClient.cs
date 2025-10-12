using System.Collections.Generic;
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
using MarkItDown.Conversion.Middleware;
using MarkItDown.Converters;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Providers.Aws;
using MarkItDown.Intelligence.Providers.Azure;
using MarkItDown.Intelligence.Providers.Google;
using MarkItDown.YouTube;
using Microsoft.Extensions.Logging;

namespace MarkItDown;

/// <summary>
/// Main class for converting various file formats to Markdown.
/// An extremely simple text-based document reader, suitable for LLM use.
/// This reader will convert common file-types or webpages to Markdown.
/// </summary>
public sealed class MarkItDownClient
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
    public void RegisterConverter(IDocumentConverter converter, double priority = ConverterPriority.SpecificFileFormat)
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
    public void RegisterConverter(IDocumentConverter converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        RegisterConverter(converter, converter.Priority);
    }

    /// <summary>
    /// Get the list of registered converters.
    /// </summary>
    /// <returns>A read-only list of registered converters.</returns>
    public IReadOnlyList<IDocumentConverter> GetRegisteredConverters()
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
        => ConvertFileInternalAsync(filePath, null, cancellationToken);

    public Task<DocumentConverterResult> ConvertAsync(string filePath, StreamInfo streamInfoOverride, CancellationToken cancellationToken = default)
        => ConvertFileInternalAsync(filePath, streamInfoOverride, cancellationToken);

    /// <summary>
    /// Convert a stream to Markdown.
    /// </summary>
    /// <param name="stream">The stream to convert.</param>
    /// <param name="streamInfo">Metadata about the stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The converted Markdown content.</returns>
    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must support seeking.", nameof(stream));

        using var activity = StartActivity(MarkItDownDiagnostics.ActivityNameConvertStream, streamInfo);
        var source = DescribeSource(streamInfo);

        var exceptions = new List<Exception>();
        var guesses = StreamInfoGuesser.Guess(stream, streamInfo);
        activity?.SetTag("markitdown.guess.count", guesses.Count);
        _logger?.LogInformation("Converting {Source} with {GuessCount} candidate formats", source, guesses.Count);

        foreach (var guess in guesses)
        {
            foreach (var registration in _converters)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }

                    if (!registration.Converter.Accepts(stream, guess, cancellationToken))
                    {
                        continue;
                    }

                    var converterName = registration.Converter.GetType().Name;
                    _logger?.LogDebug("Using converter {ConverterType} for {MimeType} {Extension}",
                        converterName,
                        guess.MimeType,
                        guess.Extension);

                    activity?.SetTag("markitdown.converter", converterName);
                    activity?.SetTag("markitdown.detected.mime", guess.MimeType);
                    activity?.SetTag("markitdown.detected.extension", guess.Extension);

                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }

                    var result = await registration.Converter.ConvertAsync(stream, guess, cancellationToken).ConfigureAwait(false);
                    RecordSuccess(guess);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    _logger?.LogInformation("Converted {Source} using {ConverterType}", source, converterName);
                    return result;
                }
                catch (Exception ex)
                {
                    var converterName = registration.Converter.GetType().Name;
                    RecordFailure(converterName, guess);
                    _logger?.LogWarning(ex, "Converter {ConverterType} failed for {Source}", converterName, source);
                    exceptions.Add(ex);
                }
            }
        }

        var message = $"No converter available for file type. MimeType: {streamInfo.MimeType}, Extension: {streamInfo.Extension}";
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
    public async Task<DocumentConverterResult> ConvertFromUrlAsync(string url, StreamInfo? streamInfoOverride = null, CancellationToken cancellationToken = default)
    {
        if (_httpClient is null)
            throw new InvalidOperationException("HTTP client is required for URL conversion. Provide one in the constructor.");

        using var activity = StartActivity(MarkItDownDiagnostics.ActivityNameConvertUrl);
        activity?.SetTag("markitdown.url", url);

        Activity? downloadActivity = null;
        try
        {
            _logger?.LogInformation("Downloading {Url}", url);
            downloadActivity = StartActivity(MarkItDownDiagnostics.ActivityNameDownload);
            downloadActivity?.SetTag("http.url", url);

            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            downloadActivity?.SetTag("http.status_code", (int)response.StatusCode);
            activity?.SetTag("http.status_code", (int)response.StatusCode);
            response.EnsureSuccessStatusCode();

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var streamInfo = CreateStreamInfoFromUrl(url, response);
            if (streamInfoOverride is not null)
            {
                streamInfo = streamInfo.CopyWith(streamInfoOverride);
            }

            if (!string.IsNullOrWhiteSpace(streamInfo.FileName))
            {
                activity?.SetTag("markitdown.filename", streamInfo.FileName);
            }

            if (!string.IsNullOrWhiteSpace(streamInfo.MimeType))
            {
                activity?.SetTag("markitdown.mime", streamInfo.MimeType);
            }

            if (!string.IsNullOrWhiteSpace(streamInfo.Extension))
            {
                activity?.SetTag("markitdown.extension", streamInfo.Extension);
            }

            activity?.SetTag("content.length", response.Content.Headers.ContentLength ?? 0);

            using var memoryStream = new MemoryStream();
            await contentStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            memoryStream.Position = 0;

            downloadActivity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return await ConvertAsync(memoryStream, streamInfo, cancellationToken).ConfigureAwait(false);
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
    public async Task<DocumentConverterResult> ConvertUriAsync(string uri, StreamInfo? streamInfo = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(uri);

        var trimmed = uri.Trim();

        if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var localPath = UriUtilities.ResolveFilePath(trimmed);
            return await ConvertFileInternalAsync(localPath, streamInfo, cancellationToken).ConfigureAwait(false);
        }

        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var payload = UriUtilities.ParseDataUri(trimmed);
            var extension = MimeMapping.GetExtension(payload.MimeType);
            var normalizedExtension = string.IsNullOrWhiteSpace(extension) ? null : (extension.StartsWith('.') ? extension : "." + extension);
            var baseInfo = new StreamInfo(
                mimeType: payload.MimeType,
                extension: normalizedExtension,
                charset: TryGetEncoding(payload.Charset));

            if (streamInfo is not null)
            {
                baseInfo = baseInfo.CopyWith(streamInfo);
            }

            using var buffer = new MemoryStream(payload.Data, writable: false);
            return await ConvertAsync(buffer, baseInfo, cancellationToken).ConfigureAwait(false);
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return await ConvertFromUrlAsync(trimmed, streamInfo, cancellationToken).ConfigureAwait(false);
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

    private IConversionPipeline BuildConversionPipeline()
    {
        var middleware = new List<IConversionMiddleware>();

        if (_options.EnableAiImageEnrichment)
        {
            middleware.Add(new AiImageEnrichmentMiddleware());
        }

        if (_options.ConversionMiddleware is { Count: > 0 })
        {
            middleware.AddRange(_options.ConversionMiddleware);
        }

        if (middleware.Count == 0)
        {
            return ConversionPipeline.Empty;
        }

        return new ConversionPipeline(middleware, _intelligenceProviders.AiModels ?? NullAiModelProvider.Instance, _logger);
    }

    private IntelligenceProviderHub InitializeIntelligenceProviders()
    {
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

    private IEnumerable<IDocumentConverter> CreateBuiltInConverters()
    {
        IDocumentConverter CreateImageConverter() => new ImageConverter(_options.ExifToolPath, _options.ImageCaptioner);
        IDocumentConverter CreateAudioConverter() => new AudioConverter(_options.ExifToolPath, _options.AudioTranscriber, _options.Segments, _intelligenceProviders.Media);

        var converters = new List<IDocumentConverter>
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
            new ZipConverter(CreateZipInnerConverters(CreateImageConverter, CreateAudioConverter)),
            new PdfConverter(_options.Segments, _intelligenceProviders.Document, _intelligenceProviders.Image, _conversionPipeline),
            new DocxConverter(_options.Segments, _conversionPipeline, _intelligenceProviders.Image),
            new XlsxConverter(_options.Segments),
            new PptxConverter(_options.Segments, _conversionPipeline, _intelligenceProviders.Image),
            CreateAudioConverter(),
            CreateImageConverter(),
            new PlainTextConverter(),
        };

        return converters;
    }

    private IEnumerable<IDocumentConverter> CreateZipInnerConverters(Func<IDocumentConverter> imageConverterFactory, Func<IDocumentConverter> audioConverterFactory)
    {
        return new IDocumentConverter[]
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
            new DocxConverter(_options.Segments, _conversionPipeline, _intelligenceProviders.Image),
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
                activity.SetTag("markitdown.mime", streamInfo.MimeType);
            }

            if (!string.IsNullOrWhiteSpace(streamInfo.Extension))
            {
                activity.SetTag("markitdown.extension", streamInfo.Extension);
            }

            if (!string.IsNullOrWhiteSpace(streamInfo.FileName))
            {
                activity.SetTag("markitdown.filename", streamInfo.FileName);
            }

            if (!string.IsNullOrWhiteSpace(streamInfo.Url))
            {
                activity.SetTag("markitdown.url", streamInfo.Url);
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
            new KeyValuePair<string, object?>("markitdown.mime", guess.MimeType ?? string.Empty),
            new KeyValuePair<string, object?>("markitdown.extension", guess.Extension ?? string.Empty));
    }

    private void RecordFailure(string converterName, StreamInfo guess)
    {
        if (_conversionFailureCounter is null)
        {
            return;
        }

        _conversionFailureCounter.Add(1,
            new KeyValuePair<string, object?>("markitdown.converter", converterName),
            new KeyValuePair<string, object?>("markitdown.mime", guess.MimeType ?? string.Empty),
            new KeyValuePair<string, object?>("markitdown.extension", guess.Extension ?? string.Empty));
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

    private async Task<DocumentConverterResult> ConvertFileInternalAsync(string filePath, StreamInfo? overrides, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        using var activity = StartActivity(MarkItDownDiagnostics.ActivityNameConvertFile);
        activity?.SetTag("markitdown.path", filePath);
        _logger?.LogInformation("Converting file {FilePath}", filePath);

        using var fileStream = File.OpenRead(filePath);
        var streamInfo = CreateStreamInfoFromFile(filePath);
        if (overrides is not null)
        {
            streamInfo = streamInfo.CopyWith(overrides);
        }

        try
        {
            var result = await ConvertAsync(fileStream, streamInfo, cancellationToken).ConfigureAwait(false);
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

    private static StreamInfo CreateStreamInfoFromUrl(string url, HttpResponseMessage response)
    {
        var uri = new Uri(url);
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
        
        return new StreamInfo(mimeType, extension, charset, filename, url);
    }

    private static string? GetMimeTypeFromExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var mime = MimeMapping.GetMimeType(extension);
        if (!string.IsNullOrWhiteSpace(mime))
        {
            return mime;
        }

        return MimeHelper.GetMimeType(extension);
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
}
