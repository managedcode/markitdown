using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.Runtime;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using System.Diagnostics.CodeAnalysis;

namespace MarkItDown.Intelligence.Providers.Aws;

/// <summary>
/// AWS Rekognition implementation of <see cref="IImageUnderstandingProvider"/>.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class AwsImageUnderstandingProvider : IImageUnderstandingProvider, IDisposable
{
    private readonly IAmazonRekognition _client;
    private readonly AwsVisionOptions _options;
    private bool _disposed;

    public AwsImageUnderstandingProvider(AwsVisionOptions options, IAmazonRekognition? client = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = client ?? CreateClient(options);
    }

    public async Task<ImageUnderstandingResult?> AnalyzeAsync(Stream stream, StreamInfo streamInfo, ImageUnderstandingRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        await using var handle = await DiskBufferHandle.FromStreamAsync(stream, streamInfo.Extension, bufferSize: 128 * 1024, onChunkWritten: null, cancellationToken).ConfigureAwait(false);
        var imageBytes = await File.ReadAllBytesAsync(handle.FilePath, cancellationToken).ConfigureAwait(false);

        var overrides = request?.Aws;
        var maxLabels = overrides?.MaxLabels ?? _options.MaxLabels;
        var minConfidence = overrides?.MinConfidence ?? _options.MinConfidence;

        using var labelStream = new MemoryStream(imageBytes, writable: false);
        using var textStream = new MemoryStream(imageBytes, writable: false);

        var labelRequest = new DetectLabelsRequest
        {
            Image = new Amazon.Rekognition.Model.Image { Bytes = labelStream },
            MaxLabels = maxLabels,
            MinConfidence = minConfidence
        };

        var textRequest = new DetectTextRequest
        {
            Image = new Amazon.Rekognition.Model.Image { Bytes = textStream }
        };

        var labelTask = _client.DetectLabelsAsync(labelRequest, cancellationToken);
        var textTask = _client.DetectTextAsync(textRequest, cancellationToken);

        await Task.WhenAll(labelTask, textTask).ConfigureAwait(false);

        var labelResponse = await labelTask.ConfigureAwait(false);
        var textResponse = await textTask.ConfigureAwait(false);

        var labels = labelResponse.Labels
            .OrderByDescending(l => l.Confidence)
            .Select(l => l.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Take(maxLabels)
            .Select(name => name!)
            .ToArray();

        var caption = labels.FirstOrDefault();

        var detectedText = textResponse.TextDetections
            .Where(t => string.Equals(t.Type, TextTypes.LINE, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.DetectedText)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        var text = detectedText.Length > 0 ? string.Join(Environment.NewLine, detectedText) : null;

        var objects = labelResponse.Labels
            .Where(l => l.Instances != null && l.Instances.Count > 0)
            .Select(l => l.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .Take(maxLabels)
            .Select(name => name!)
            .ToArray();

        var metadata = new Dictionary<string, string>
        {
            [MetadataKeys.Provider] = MetadataValues.ProviderAwsRekognition
        };

        if (labelResponse.LabelModelVersion is not null)
        {
            metadata[MetadataKeys.ModelVersion] = labelResponse.LabelModelVersion;
        }

        return new ImageUnderstandingResult(caption, text, labels, objects, metadata);
    }

    private static IAmazonRekognition CreateClient(AwsVisionOptions options)
    {
        var region = ResolveRegion(options.Region);

        if (options.Credentials is not null)
        {
            return new AmazonRekognitionClient(options.Credentials, region);
        }

        if (!string.IsNullOrWhiteSpace(options.AccessKeyId) && !string.IsNullOrWhiteSpace(options.SecretAccessKey))
        {
            AWSCredentials credentials = string.IsNullOrWhiteSpace(options.SessionToken)
                ? new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey)
                : new SessionAWSCredentials(options.AccessKeyId, options.SecretAccessKey, options.SessionToken);

            return new AmazonRekognitionClient(credentials, region);
        }

        return new AmazonRekognitionClient(region);
    }

    private static RegionEndpoint ResolveRegion(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? RegionEndpoint.USEast1 : RegionEndpoint.GetBySystemName(value);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
    }
}
