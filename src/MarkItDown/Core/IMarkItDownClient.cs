namespace MarkItDown;

/// <summary>
/// Abstraction for <see cref="MarkItDownClient"/> to enable dependency injection scenarios.
/// </summary>
public interface IMarkItDownClient
{
    Task<DocumentConverterResult> ConvertAsync(string filePath, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertAsync(string filePath, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertAsync(string filePath, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertAsync(string filePath, IProgress<ConversionProgress>? progress, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertAsync(string filePath, ConversionRequest request, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertAsync(string filePath, IProgress<ConversionProgress>? progress, ConversionRequest request, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, ConversionRequest request, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, ConversionRequest request, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertFromUrlAsync(string url, StreamInfo? streamInfoOverride = null, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertFromUrlAsync(string url, StreamInfo? streamInfoOverride, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertFromUrlAsync(string url, StreamInfo? streamInfoOverride, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertFromUrlAsync(string url, StreamInfo? streamInfoOverride, IProgress<ConversionProgress>? progress, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertFromUrlAsync(string url, StreamInfo? streamInfoOverride, ConversionRequest request, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertFromUrlAsync(string url, StreamInfo? streamInfoOverride, IProgress<ConversionProgress>? progress, ConversionRequest request, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertFromUrlAsync(Uri url, StreamInfo? streamInfoOverride = null, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertFromUrlAsync(Uri url, StreamInfo? streamInfoOverride, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertFromUrlAsync(Uri url, StreamInfo? streamInfoOverride, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertFromUrlAsync(Uri url, StreamInfo? streamInfoOverride, IProgress<ConversionProgress>? progress, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertFromUrlAsync(Uri url, StreamInfo? streamInfoOverride, ConversionRequest request, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertFromUrlAsync(Uri url, StreamInfo? streamInfoOverride, IProgress<ConversionProgress>? progress, ConversionRequest request, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertUriAsync(string uri, StreamInfo? streamInfo = null, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertUriAsync(string uri, StreamInfo? streamInfo, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertUriAsync(string uri, StreamInfo? streamInfo, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertUriAsync(string uri, StreamInfo? streamInfo, IProgress<ConversionProgress>? progress, Action<ConversionRequestBuilder> configure, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertUriAsync(string uri, StreamInfo? streamInfo, ConversionRequest request, CancellationToken cancellationToken = default);

    Task<DocumentConverterResult> ConvertUriAsync(string uri, StreamInfo? streamInfo, IProgress<ConversionProgress>? progress, ConversionRequest request, CancellationToken cancellationToken = default);
}
