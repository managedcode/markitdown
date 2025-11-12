using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using Google.Api.Gax.Grpc;
using Google.Cloud.Vision.V1;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;

namespace MarkItDown.Intelligence.Providers.Google;

/// <summary>
/// Google Vision implementation of <see cref="IImageUnderstandingProvider"/>.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class GoogleImageUnderstandingProvider : IImageUnderstandingProvider
{
    private readonly ImageAnnotatorClient _client;
    private readonly GoogleVisionOptions _options;

    public GoogleImageUnderstandingProvider(GoogleVisionOptions options, ImageAnnotatorClient? client = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (client is not null)
        {
            _client = client;
        }
        else
        {
            var builder = new ImageAnnotatorClientBuilder();

            var credential = GoogleCredentialResolver.Resolve(
                options.Credential,
                options.JsonCredentials,
                options.CredentialsPath,
                ImageAnnotatorClient.DefaultScopes);

            if (credential is not null)
            {
                builder.Credential = credential;
            }

            _client = builder.Build();
        }
    }

    public async Task<ImageUnderstandingResult?> AnalyzeAsync(Stream stream, StreamInfo streamInfo, ImageUnderstandingRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        await using var handle = await DiskBufferHandle.FromStreamAsync(stream, streamInfo.Extension, bufferSize: 128 * 1024, onChunkWritten: null, cancellationToken).ConfigureAwait(false);
        var bytes = await File.ReadAllBytesAsync(handle.FilePath, cancellationToken).ConfigureAwait(false);

        var image = Image.FromBytes(bytes);

        var annotateRequest = new AnnotateImageRequest
        {
            Image = image
        };

        var overrides = request?.Google;
        var features = ResolveFeatures(overrides?.FeatureHints, _options.MaxLabels);
        foreach (var feature in features)
        {
            annotateRequest.Features.Add(feature);
        }

        var batchRequest = new BatchAnnotateImagesRequest();
        batchRequest.Requests.Add(annotateRequest);

        var callSettings = CallSettings.FromCancellationToken(cancellationToken);
        var response = await _client.BatchAnnotateImagesAsync(batchRequest, callSettings).ConfigureAwait(false);
        var annotation = response.Responses.FirstOrDefault();

        if (annotation is null)
        {
            return null;
        }

        if (annotation.Error is not null && annotation.Error.Code > 0)
        {
            throw new InvalidOperationException($"Google Vision returned an error: {annotation.Error.Message} (code {annotation.Error.Code}).");
        }

        var minConfidence = overrides?.MinConfidence ?? _options.MinConfidence;

        var labels = annotation.LabelAnnotations
            .Where(l => l.Score * 100f >= minConfidence)
            .OrderByDescending(l => l.Score)
            .Select(l => l.Description)
            .Where(static l => !string.IsNullOrWhiteSpace(l))
            .Take(_options.MaxLabels)
            .Select(l => l!)
            .ToArray();

        var caption = labels.FirstOrDefault();
        var text = annotation.FullTextAnnotation?.Text;

        var objects = annotation.LocalizedObjectAnnotations
            .OrderByDescending(o => o.Score)
            .Select(o => o.Name)
            .Where(static n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .Take(_options.MaxLabels)
            .Select(n => n!)
            .ToArray();

        var metadata = new Dictionary<string, string>();
        if (annotation.SafeSearchAnnotation is not null)
        {
            metadata[MetadataKeys.SafeSearchAdult] = annotation.SafeSearchAnnotation.Adult.ToString();
            metadata[MetadataKeys.SafeSearchMedical] = annotation.SafeSearchAnnotation.Medical.ToString();
            metadata[MetadataKeys.SafeSearchRacy] = annotation.SafeSearchAnnotation.Racy.ToString();
            metadata[MetadataKeys.SafeSearchViolence] = annotation.SafeSearchAnnotation.Violence.ToString();
        }

        return new ImageUnderstandingResult(caption, text, labels, objects, metadata);
    }

    private static IEnumerable<Feature> ResolveFeatures(IReadOnlyList<string>? featureHints, int maxLabels)
    {
        if (featureHints is null || featureHints.Count == 0)
        {
            yield return new Feature { Type = Feature.Types.Type.LabelDetection, MaxResults = maxLabels };
            yield return new Feature { Type = Feature.Types.Type.TextDetection };
            yield return new Feature { Type = Feature.Types.Type.ObjectLocalization, MaxResults = maxLabels };
            yield return new Feature { Type = Feature.Types.Type.SafeSearchDetection };
            yield break;
        }

        foreach (var hint in featureHints)
        {
            if (string.IsNullOrWhiteSpace(hint))
            {
                continue;
            }

            if (Enum.TryParse<Feature.Types.Type>(hint, ignoreCase: true, out var parsed))
            {
                yield return new Feature { Type = parsed, MaxResults = maxLabels };
            }
        }
    }
}
