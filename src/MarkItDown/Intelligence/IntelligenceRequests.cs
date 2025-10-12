using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MarkItDown.Intelligence;

public enum DocumentIntelligenceProviderKind
{
    Azure,
    Google,
    Aws,
    Custom
}

public sealed record DocumentIntelligenceRequest(
    DocumentIntelligenceProviderKind? PreferredProvider,
    AzureDocumentIntelligenceOverrides? Azure,
    GoogleDocumentIntelligenceOverrides? Google,
    AwsDocumentIntelligenceOverrides? Aws,
    string? CustomProviderName = null);

public sealed record AzureDocumentIntelligenceOverrides(
    string? ModelId,
    string? Endpoint = null,
    string? ApiKey = null);

public sealed record GoogleDocumentIntelligenceOverrides(
    string? ProjectId,
    string? Location,
    string? ProcessorId,
    string? Endpoint = null);

public sealed record AwsDocumentIntelligenceOverrides(
    IReadOnlyList<string> FeatureTypes,
    string? Region = null);

public sealed class AzureDocumentIntelligenceRequestBuilder
{
    private string? modelId;
    private string? endpoint;
    private string? apiKey;

    public AzureDocumentIntelligenceRequestBuilder UseModel(string model)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);
        modelId = model;
        return this;
    }

    public AzureDocumentIntelligenceRequestBuilder WithEndpoint(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        endpoint = value;
        return this;
    }

    public AzureDocumentIntelligenceRequestBuilder WithApiKey(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        apiKey = value;
        return this;
    }

    internal DocumentIntelligenceRequest Build()
    {
        return new DocumentIntelligenceRequest(
            DocumentIntelligenceProviderKind.Azure,
            new AzureDocumentIntelligenceOverrides(modelId, endpoint, apiKey),
            null,
            null);
    }
}

public sealed class GoogleDocumentIntelligenceRequestBuilder
{
    private string? projectId;
    private string? location;
    private string? processorId;
    private string? endpoint;

    public GoogleDocumentIntelligenceRequestBuilder WithProject(string project)
    {
        ArgumentException.ThrowIfNullOrEmpty(project);
        projectId = project;
        return this;
    }

    public GoogleDocumentIntelligenceRequestBuilder WithLocation(string region)
    {
        ArgumentException.ThrowIfNullOrEmpty(region);
        location = region;
        return this;
    }

    public GoogleDocumentIntelligenceRequestBuilder UseProcessor(string processor)
    {
        ArgumentException.ThrowIfNullOrEmpty(processor);
        processorId = processor;
        return this;
    }

    public GoogleDocumentIntelligenceRequestBuilder OverrideEndpoint(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        endpoint = value;
        return this;
    }

    internal DocumentIntelligenceRequest Build()
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(processorId))
        {
            throw new InvalidOperationException("ProjectId, Location, and ProcessorId must be configured for Google Document AI.");
        }

        return new DocumentIntelligenceRequest(
            DocumentIntelligenceProviderKind.Google,
            null,
            new GoogleDocumentIntelligenceOverrides(projectId, location, processorId, endpoint),
            null);
    }
}

public sealed class AwsDocumentIntelligenceRequestBuilder
{
    private readonly HashSet<string> featureTypes = new(StringComparer.OrdinalIgnoreCase);
    private string? region;

    public AwsDocumentIntelligenceRequestBuilder WithRegion(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        region = value;
        return this;
    }

    public AwsDocumentIntelligenceRequestBuilder AddFeature(string featureType)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureType);
        featureTypes.Add(featureType);
        return this;
    }

    public AwsDocumentIntelligenceRequestBuilder UseFormsAndTables()
    {
        featureTypes.Add("FORMS");
        featureTypes.Add("TABLES");
        return this;
    }

    internal DocumentIntelligenceRequest Build()
    {
        var features = featureTypes.Count == 0
            ? new List<string> { "FORMS", "TABLES" }
            : featureTypes.ToList();

        return new DocumentIntelligenceRequest(
            DocumentIntelligenceProviderKind.Aws,
            null,
            null,
            new AwsDocumentIntelligenceOverrides(new ReadOnlyCollection<string>(features), region));
    }
}

public enum ImageUnderstandingProviderKind
{
    Azure,
    Google,
    Aws,
    Custom
}

public sealed record ImageUnderstandingRequest(
    ImageUnderstandingProviderKind? PreferredProvider,
    AzureImageUnderstandingOverrides? Azure,
    GoogleImageUnderstandingOverrides? Google,
    AwsImageUnderstandingOverrides? Aws,
    string? CustomProviderName = null);

public sealed record AzureImageUnderstandingOverrides(
    IReadOnlyList<string>? VisualFeatures,
    string? ModelVersion = null);

public sealed record GoogleImageUnderstandingOverrides(
    IReadOnlyList<string>? FeatureHints,
    float? MinConfidence = null);

public sealed record AwsImageUnderstandingOverrides(
    float? MinConfidence,
    int? MaxLabels = null);

public enum MediaTranscriptionProviderKind
{
    Azure,
    Google,
    Aws,
    Custom
}

public sealed record MediaTranscriptionRequest(
    MediaTranscriptionProviderKind? PreferredProvider,
    string? Language,
    string? VocabularyName = null,
    string? CustomProviderName = null);
