using System.Globalization;
using System.Linq;
using System.Text;
using Azure;
using Azure.AI.Vision.ImageAnalysis;
using Azure.Identity;
using MarkItDown;
using MarkItDown.Intelligence.Models;

namespace MarkItDown.Intelligence.Providers.Azure;

/// <summary>
/// Azure Computer Vision provider for image OCR and captioning.
/// </summary>
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

    public async Task<ImageUnderstandingResult?> AnalyzeAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        memory.Position = 0;

        var visualFeatures = VisualFeatures.Caption | VisualFeatures.DenseCaptions | VisualFeatures.Objects | VisualFeatures.Tags | VisualFeatures.Read;
        var response = await _client.AnalyzeAsync(BinaryData.FromStream(memory), visualFeatures, new ImageAnalysisOptions(), cancellationToken).ConfigureAwait(false);
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
}
