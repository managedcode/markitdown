using System.Globalization;
using System.Linq;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.AI.Vision.ImageAnalysis;
using Azure.Identity;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;

namespace MarkItDown.Intelligence.Providers.Azure;

/// <summary>
/// Azure Computer Vision provider for image OCR and captioning.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class AzureImageUnderstandingProvider : IImageUnderstandingProvider
{
    private readonly ImageAnalysisClient _client;

    public AzureImageUnderstandingProvider(AzureVisionOptions options, ImageAnalysisClient? client = null)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new ArgumentException("Azure Vision endpoint must be provided.", nameof(options));
        }

        if (client is not null)
        {
            _client = client;
        }
        else if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            _client = new ImageAnalysisClient(new Uri(options.Endpoint), new AzureKeyCredential(options.ApiKey));
        }
        else
        {
            _client = new ImageAnalysisClient(new Uri(options.Endpoint), new DefaultAzureCredential());
        }
    }

    public async Task<ImageUnderstandingResult?> AnalyzeAsync(Stream stream, StreamInfo streamInfo, ImageUnderstandingRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        await using var handle = await DiskBufferHandle.FromStreamAsync(stream, streamInfo.Extension, bufferSize: 128 * 1024, onChunkWritten: null, cancellationToken).ConfigureAwait(false);
        await using var localStream = handle.OpenRead();

        var overrides = request?.Azure;
        var visualFeatures = ResolveVisualFeatures(overrides?.VisualFeatures);
        var options = new ImageAnalysisOptions();
        if (!string.IsNullOrWhiteSpace(overrides?.ModelVersion))
        {
            options.ModelVersion = overrides.ModelVersion;
        }

        var response = await _client.AnalyzeAsync(BinaryData.FromStream(localStream), visualFeatures, options, cancellationToken).ConfigureAwait(false);
        var analysis = response.Value;

        var caption = analysis.Caption?.Text;
        if (string.IsNullOrWhiteSpace(caption) && analysis.DenseCaptions?.Values?.Count > 0)
        {
            caption = analysis.DenseCaptions.Values[0].Text;
        }

        var ocrBuilder = new StringBuilder();
        if (analysis.Read?.Blocks is not null)
        {
            foreach (var block in analysis.Read.Blocks)
            {
                foreach (var line in block.Lines)
                {
                    if (!string.IsNullOrWhiteSpace(line.Text))
                    {
                        ocrBuilder.AppendLine(line.Text);
                    }
                }
            }
        }

        var ocrText = ocrBuilder.Length > 0 ? ocrBuilder.ToString().TrimEnd() : null;
        var tags = analysis.Tags?.Values?.Select(t => t.Name).ToArray() ?? Array.Empty<string>();
        var objects = analysis.Objects?.Values?
            .SelectMany(o => o.Tags?.Select(t => t.Name) ?? Array.Empty<string>())
            .Distinct()
            .ToArray() ?? Array.Empty<string>();

        var metadata = new Dictionary<string, string>();
        if (analysis.Metadata != null)
        {
            metadata[MetadataKeys.Width] = analysis.Metadata.Width.ToString(CultureInfo.InvariantCulture);
            metadata[MetadataKeys.Height] = analysis.Metadata.Height.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(analysis.ModelVersion))
        {
            metadata[MetadataKeys.ModelVersion] = analysis.ModelVersion;
        }

        return new ImageUnderstandingResult(caption, ocrText, tags, objects, metadata);
    }

    private static VisualFeatures ResolveVisualFeatures(IReadOnlyList<string>? requested)
    {
        if (requested is null || requested.Count == 0)
        {
            return VisualFeatures.Caption | VisualFeatures.DenseCaptions | VisualFeatures.Objects | VisualFeatures.Tags | VisualFeatures.Read;
        }

        var features = VisualFeatures.None;
        foreach (var item in requested)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            if (Enum.TryParse<VisualFeatures>(item, ignoreCase: true, out var parsed))
            {
                features |= parsed;
            }
        }

        return features == VisualFeatures.None
            ? VisualFeatures.Caption | VisualFeatures.DenseCaptions | VisualFeatures.Objects | VisualFeatures.Tags | VisualFeatures.Read
            : features;
    }
}
