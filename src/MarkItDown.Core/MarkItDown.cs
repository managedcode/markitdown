using MarkItDown.Core.Converters;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MarkItDown.Core;

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

    /// <summary>
    /// Initialize a new instance of MarkItDown.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic information.</param>
    /// <param name="httpClient">Optional HTTP client for downloading web content.</param>
    public MarkItDown(ILogger? logger = null, HttpClient? httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient;
        _converters = new List<ConverterRegistration>();

        // Register built-in converters
        RegisterBuiltInConverters();
    }

    /// <summary>
    /// Register a custom converter.
    /// </summary>
    /// <param name="converter">The converter to register.</param>
    /// <param name="priority">The priority of the converter. Lower values are tried first.</param>
    public void RegisterConverter(IDocumentConverter converter, double priority = ConverterPriority.SpecificFileFormat)
    {
        _converters.Add(new ConverterRegistration(converter, priority));
        
        // Sort by priority to ensure proper order
        _converters.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    /// <summary>
    /// Convert a file to Markdown.
    /// </summary>
    /// <param name="filePath">Path to the file to convert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The converted Markdown content.</returns>
    public async Task<DocumentConverterResult> ConvertAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        using var fileStream = File.OpenRead(filePath);
        var streamInfo = CreateStreamInfoFromFile(filePath);
        
        return await ConvertAsync(fileStream, streamInfo, cancellationToken);
    }

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

        foreach (var registration in _converters)
        {
            try
            {
                // Reset stream position before checking
                stream.Position = 0;

                if (registration.Converter.Accepts(stream, streamInfo, cancellationToken))
                {
                    _logger?.LogDebug("Using converter {ConverterType} for {MimeType} {Extension}", 
                        registration.Converter.GetType().Name, 
                        streamInfo.MimeType, 
                        streamInfo.Extension);

                    // Reset stream position before conversion
                    stream.Position = 0;
                    
                    return await registration.Converter.ConvertAsync(stream, streamInfo, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Converter {ConverterType} failed", registration.Converter.GetType().Name);
                exceptions.Add(ex);
            }
        }

        // If no converter could handle the file, throw an exception
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
    public async Task<DocumentConverterResult> ConvertFromUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        if (_httpClient is null)
            throw new InvalidOperationException("HTTP client is required for URL conversion. Provide one in the constructor.");

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var streamInfo = CreateStreamInfoFromUrl(url, response);

        // Copy to memory stream to ensure we can seek
        using var memoryStream = new MemoryStream();
        await contentStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        return await ConvertAsync(memoryStream, streamInfo, cancellationToken);
    }

    private void RegisterBuiltInConverters()
    {
        // Register converters in order of priority (lower priority values first)
        RegisterConverter(new HtmlConverter(), ConverterPriority.SpecificFileFormat);
        
        // Register the plain text converter with generic priority (catch-all)
        RegisterConverter(new PlainTextConverter(), ConverterPriority.GenericFileFormat);
        
        // TODO: Add other converters here as they are implemented
        // RegisterConverter(new PdfConverter(), ConverterPriority.SpecificFileFormat);
        // RegisterConverter(new DocxConverter(), ConverterPriority.SpecificFileFormat);
        // etc.
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
            ".xml" => "application/xml",
            ".csv" => "text/csv",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ => null
        };
    }
}