using System.Text;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Core;
using Azure.Identity;
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
public sealed class MarkItDown
{
    private readonly List<ConverterRegistration> _converters;
    private readonly ILogger? _logger;
    private readonly HttpClient? _httpClient;
    private readonly MarkItDownOptions _options;
    private readonly IntelligenceProviderHub _intelligenceProviders;
    private readonly IYouTubeMetadataProvider _youTubeMetadataProvider;

    /// <summary>
    /// Initialize a new instance of MarkItDown.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic information.</param>
    /// <param name="httpClient">Optional HTTP client for downloading web content.</param>
    public MarkItDown(ILogger? logger = null, HttpClient? httpClient = null)
        : this(null, logger, httpClient)
    {
    }

    /// <summary>
    /// Initialize a new instance of MarkItDown with advanced configuration options.
    /// </summary>
    /// <param name="options">Configuration overrides for the converter. When <see langword="null"/> defaults are used.</param>
    /// <param name="logger">Optional logger for diagnostic information.</param>
    /// <param name="httpClient">Optional HTTP client for downloading web content.</param>
    public MarkItDown(MarkItDownOptions? options, ILogger? logger = null, HttpClient? httpClient = null)
    {
        _options = options ?? new MarkItDownOptions();
        _logger = logger;
        _httpClient = httpClient;
        _converters = [];
        _intelligenceProviders = InitializeIntelligenceProviders();
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

        var exceptions = new List<Exception>();
        var guesses = StreamInfoGuesser.Guess(stream, streamInfo);

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

                    _logger?.LogDebug("Using converter {ConverterType} for {MimeType} {Extension}",
                        registration.Converter.GetType().Name,
                        guess.MimeType,
                        guess.Extension);

                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }

                    return await registration.Converter.ConvertAsync(stream, guess, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Converter {ConverterType} failed", registration.Converter.GetType().Name);
                    exceptions.Add(ex);
                }
            }
        }

        var message = $"No converter available for file type. MimeType: {streamInfo.MimeType}, Extension: {streamInfo.Extension}";

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

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var streamInfo = CreateStreamInfoFromUrl(url, response);
        if (streamInfoOverride is not null)
        {
            streamInfo = streamInfo.CopyWith(streamInfoOverride);
        }

        // Copy to memory stream to ensure we can seek
        using var memoryStream = new MemoryStream();
        await contentStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        return await ConvertAsync(memoryStream, streamInfo, cancellationToken);
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
            new JupyterNotebookConverter(),
            new CsvConverter(),
            new EpubConverter(),
            new EmlConverter(),
            new XmlConverter(),
            new ZipConverter(CreateZipInnerConverters(CreateImageConverter, CreateAudioConverter)),
            new PdfConverter(_options.Segments, _intelligenceProviders.Document, _intelligenceProviders.Image),
            new DocxConverter(_options.Segments),
            new XlsxConverter(_options.Segments),
            new PptxConverter(_options.Segments),
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
            new JupyterNotebookConverter(),
            new CsvConverter(),
            new EmlConverter(),
            new XmlConverter(),
            new PdfConverter(_options.Segments, _intelligenceProviders.Document, _intelligenceProviders.Image),
            new DocxConverter(_options.Segments),
            new XlsxConverter(_options.Segments),
            new PptxConverter(_options.Segments),
            audioConverterFactory(),
            imageConverterFactory(),
            new PlainTextConverter(),
        };
    }

    private async Task<DocumentConverterResult> ConvertFileInternalAsync(string filePath, StreamInfo? overrides, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        using var fileStream = File.OpenRead(filePath);
        var streamInfo = CreateStreamInfoFromFile(filePath);
        if (overrides is not null)
        {
            streamInfo = streamInfo.CopyWith(overrides);
        }

        return await ConvertAsync(fileStream, streamInfo, cancellationToken).ConfigureAwait(false);
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
            return extension?.ToLowerInvariant() switch
            {
                ".txt" => "text/plain",
                ".md" => "text/markdown",
            ".markdown" => "text/markdown",
            ".html" => "text/html",
            ".htm" => "text/html",
            ".json" => "application/json",
            ".jsonl" => "application/json",
            ".ndjson" => "application/json",
            ".ipynb" => "application/x-ipynb+json",
            ".xml" => "application/xml",
            ".xsd" => "application/xml",
            ".xsl" => "application/xml",
            ".xslt" => "application/xml",
            ".rss" => "application/rss+xml",
            ".atom" => "application/atom+xml",
            ".csv" => "text/csv",
            ".zip" => "application/zip",
            ".epub" => "application/epub+zip",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tiff" => "image/tiff",
            ".tif" => "image/tiff",
            ".webp" => "image/webp",
                _ => MimeMapping.GetMimeType(extension)
            };
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
