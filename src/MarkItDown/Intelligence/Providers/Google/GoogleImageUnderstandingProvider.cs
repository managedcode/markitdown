using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Api.Gax.Grpc;
using Google.Cloud.Vision.V1;
using MarkItDown;
using MarkItDown.Intelligence.Models;

namespace MarkItDown.Intelligence.Providers.Google;

/// <summary>
/// Google Vision implementation of <see cref="IImageUnderstandingProvider"/>.
/// </summary>
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

            if (options.Credential is not null)
            {
                builder.Credential = options.Credential;
            }
            else if (!string.IsNullOrWhiteSpace(options.JsonCredentials))
            {
                builder.JsonCredentials = options.JsonCredentials;
            }
            else if (!string.IsNullOrWhiteSpace(options.CredentialsPath))
            {
                builder.CredentialsPath = options.CredentialsPath;
            }

            _client = builder.Build();
        }
    }

    public async Task<ImageUnderstandingResult?> AnalyzeAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        memory.Position = 0;

        var image = Image.FromBytes(memory.ToArray());

        var request = new AnnotateImageRequest
        {
            Image = image
        };

        request.Features.Add(new Feature { Type = Feature.Types.Type.LabelDetection, MaxResults = _options.MaxLabels });
        request.Features.Add(new Feature { Type = Feature.Types.Type.TextDetection });
        request.Features.Add(new Feature { Type = Feature.Types.Type.ObjectLocalization, MaxResults = _options.MaxLabels });
        request.Features.Add(new Feature { Type = Feature.Types.Type.SafeSearchDetection });

        var batchRequest = new BatchAnnotateImagesRequest();
        batchRequest.Requests.Add(request);

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

        var labels = annotation.LabelAnnotations
            .Where(l => l.Score * 100f >= _options.MinConfidence)
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
}
