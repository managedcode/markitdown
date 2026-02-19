using System;
using System.IO;
using System.Text.Json;
using ManagedCode.MimeTypes;
using MarkItDown;
using MarkItDown.Intelligence.Providers.Azure;

namespace MarkItDown.Intelligence.Configuration;

/// <summary>
/// Options controlling how Azure integration configuration is loaded.
/// </summary>
public sealed class AzureIntegrationConfigurationOptions
{
    /// <summary>
    /// Gets a default instance.
    /// </summary>
    public static AzureIntegrationConfigurationOptions Default { get; } = new();

    /// <summary>
    /// Resolver used to locate sample assets referenced by configuration entries.
    /// </summary>
    public Func<string?, string, string?> SampleResolver { get; init; } = DefaultAzureIntegrationSampleResolver.Resolve;

    /// <summary>
    /// Optional delegate invoked when configuration JSON is not supplied via environment variables.
    /// </summary>
    public Func<string?>? DefaultConfigurationFactory { get; init; }
}

internal static class DefaultAzureIntegrationSampleResolver
{
    public static string? Resolve(string? value, string defaultAsset)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var resolved = TryResolveCandidate(value);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        if (!string.IsNullOrWhiteSpace(defaultAsset))
        {
            var resolvedDefault = TryResolveCandidate(defaultAsset);
            if (resolvedDefault is not null)
            {
                return resolvedDefault;
            }

            var testFiles = Path.Combine(AppContext.BaseDirectory, "TestFiles", defaultAsset);
            if (File.Exists(testFiles))
            {
                return testFiles;
            }
        }

        return null;
    }

    private static string? TryResolveCandidate(string candidate)
    {
        var trimmed = candidate.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return File.Exists(trimmed) ? trimmed : null;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var fromBase = Path.Combine(baseDirectory, trimmed);
        if (File.Exists(fromBase))
        {
            return fromBase;
        }

        var fromCwd = Path.Combine(Environment.CurrentDirectory, trimmed);
        return File.Exists(fromCwd) ? fromCwd : null;
    }
}

internal static class AzureIntegrationDefaults
{
    public const string DefaultDocumentSample = "autogen-trial-transcript.pdf";
    public const string DefaultVisionSample = "architecture-diagram.jpg";
    public const string DefaultMediaSample = "meeting-audio.wav";
    public const string DefaultImageSample = "architecture-diagram.jpg";
    public const string DefaultAudioSample = "meeting-audio.wav";
}

/// <summary>
/// Aggregated Azure integration settings covering vision, media, and language model services.
/// </summary>
public sealed class AzureIntegrationSettings
{
    private AzureIntegrationSettings(DocumentSettings? document, VisionSettings? vision, MediaSettings? media, LanguageModelsSettings? languageModels)
    {
        Document = document;
        Vision = vision;
        Media = media;
        LanguageModels = languageModels;
    }

    /// <summary>
    /// Document Intelligence configuration, when available.
    /// </summary>
    public DocumentSettings? Document { get; }

    /// <summary>
    /// Computer Vision configuration, when available.
    /// </summary>
    public VisionSettings? Vision { get; }

    /// <summary>
    /// Video Indexer / media configuration, when available.
    /// </summary>
    public MediaSettings? Media { get; }

    /// <summary>
    /// Language model configuration, when available.
    /// </summary>
    public LanguageModelsSettings? LanguageModels { get; }

    /// <summary>
    /// Loads integration settings using the supplied options.
    /// </summary>
    public static AzureIntegrationSettings Load(AzureIntegrationConfigurationOptions? options = null)
    {
        options ??= AzureIntegrationConfigurationOptions.Default;

        var root = AzureIntegrationConfigurationSource.LoadRootElement(options);
        var resolver = options.SampleResolver ?? DefaultAzureIntegrationSampleResolver.Resolve;

        var document = DocumentSettings.TryCreate(root, resolver);
        var vision = VisionSettings.TryCreate(root, resolver);
        var media = MediaSettings.TryCreate(root, resolver);
        var languageModels = LanguageModelsSettings.TryCreate(root, resolver);

        return new AzureIntegrationSettings(document, vision, media, languageModels);
    }
}

/// <summary>
/// Common base for integration asset settings that carry sample paths.
/// </summary>
public abstract class AzureIntegrationAssetSettings
{
    protected AzureIntegrationAssetSettings(string samplePath, string mimeType, string extension)
    {
        SamplePath = samplePath;
        MimeType = mimeType;
        Extension = extension;
    }

    public string SamplePath { get; }

    public string MimeType { get; }

    public string Extension { get; }

    public FileStream OpenSample() => File.OpenRead(SamplePath);

    public StreamInfo CreateStreamInfo()
    {
        return new StreamInfo(
            mimeType: MimeType,
            extension: Extension,
            fileName: Path.GetFileName(SamplePath));
    }

    protected static bool TryResolve(string samplePath, out string mimeType, out string extension)
    {
        extension = Path.GetExtension(samplePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            mimeType = string.Empty;
            return false;
        }

        var resolved = MimeHelper.GetMimeType(extension);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            mimeType = string.Empty;
            return false;
        }

        mimeType = resolved;
        extension = EnsureLeadingDot(extension);
        return true;
    }

    protected static string EnsureLeadingDot(string extension)
        => extension.StartsWith('.') ? extension : "." + extension.TrimStart('.');
}

/// <summary>
/// Document Intelligence configuration snapshot.
/// </summary>
public sealed class DocumentSettings : AzureIntegrationAssetSettings
{
    private const string DocumentEndpointVariable = "AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT";
    private const string DocumentKeyVariable = "AZURE_DOCUMENT_INTELLIGENCE_KEY";
    private const string DocumentModelVariable = "AZURE_DOCUMENT_INTELLIGENCE_MODEL_ID";
    private const string DocumentSampleVariable = "AZURE_DOCUMENT_INTELLIGENCE_SAMPLE_ASSET";
    private const string LegacyDocumentSampleVariable = "AZURE_DOCUMENT_INTELLIGENCE_SAMPLE_PDF";

    private DocumentSettings(AzureDocumentIntelligenceOptions options, string samplePath, string mimeType, string extension)
        : base(samplePath, mimeType, extension)
    {
        Options = options;
    }

    public AzureDocumentIntelligenceOptions Options { get; }

    public static DocumentSettings? TryCreate(JsonElement? root, Func<string?, string, string?> sampleResolver)
    {
        var section = AzureIntegrationConfigurationReader.TryGetVisionSection(root, "DocumentIntelligence");

        var endpoint = AzureIntegrationConfigurationReader.GetString(section, "Endpoint");
        var apiKey = AzureIntegrationConfigurationReader.GetString(section, "ApiKey");
        var modelId = AzureIntegrationConfigurationReader.GetString(section, "ModelId") ?? "prebuilt-layout";

        endpoint = OverrideFromEnvironment(DocumentEndpointVariable, endpoint);
        apiKey = OverrideFromEnvironment(DocumentKeyVariable, apiKey);
        modelId = OverrideFromEnvironment(DocumentModelVariable, modelId) ?? "prebuilt-layout";

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var options = new AzureDocumentIntelligenceOptions
        {
            Endpoint = endpoint,
            ApiKey = apiKey,
            ModelId = modelId
        };

        var sampleSource = Environment.GetEnvironmentVariable(DocumentSampleVariable)
            ?? Environment.GetEnvironmentVariable(LegacyDocumentSampleVariable);
        var samplePath = sampleResolver(sampleSource, AzureIntegrationDefaults.DefaultDocumentSample);

        if (string.IsNullOrWhiteSpace(samplePath))
        {
            return null;
        }

        if (!TryResolve(samplePath, out var mimeType, out var extension))
        {
            return null;
        }

        return new DocumentSettings(options, samplePath, mimeType, extension);
    }

    private static string? OverrideFromEnvironment(string variableName, string? fallback)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}

/// <summary>
/// Computer Vision configuration snapshot.
/// </summary>
public sealed class VisionSettings : AzureIntegrationAssetSettings
{
    private const string VisionEndpointVariable = "AZURE_VISION_ENDPOINT";
    private const string VisionKeyVariable = "AZURE_VISION_KEY";
    private const string VisionSampleVariable = "AZURE_VISION_SAMPLE_ASSET";
    private const string LegacyVisionSampleVariable = "AZURE_VISION_SAMPLE_IMAGE";

    private VisionSettings(AzureVisionOptions options, string samplePath, string mimeType, string extension)
        : base(samplePath, mimeType, extension)
    {
        Options = options;
    }

    public AzureVisionOptions Options { get; }

    public static VisionSettings? TryCreate(JsonElement? root, Func<string?, string, string?> sampleResolver)
    {
        var section = AzureIntegrationConfigurationReader.TryGetVisionSection(root, "ComputerVision");

        var endpoint = AzureIntegrationConfigurationReader.GetString(section, "Endpoint");
        var apiKey = AzureIntegrationConfigurationReader.GetString(section, "ApiKey");

        endpoint = OverrideFromEnvironment(VisionEndpointVariable, endpoint);
        apiKey = OverrideFromEnvironment(VisionKeyVariable, apiKey);

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var options = new AzureVisionOptions
        {
            Endpoint = endpoint,
            ApiKey = apiKey
        };

        var sampleSource = Environment.GetEnvironmentVariable(VisionSampleVariable)
            ?? Environment.GetEnvironmentVariable(LegacyVisionSampleVariable);
        var samplePath = sampleResolver(sampleSource, AzureIntegrationDefaults.DefaultVisionSample);

        if (string.IsNullOrWhiteSpace(samplePath))
        {
            return null;
        }

        if (!TryResolve(samplePath, out var mimeType, out var extension))
        {
            return null;
        }

        return new VisionSettings(options, samplePath, mimeType, extension);
    }

    private static string? OverrideFromEnvironment(string variableName, string? fallback)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}

/// <summary>
/// Azure Video Indexer configuration snapshot.
/// </summary>
public sealed class MediaSettings : AzureIntegrationAssetSettings
{
    private const string VideoSampleVariable = "AZURE_VIDEO_INDEXER_SAMPLE_ASSET";
    private const string LegacyVideoSampleVariable = "AZURE_VIDEO_INDEXER_SAMPLE_MEDIA";

    private MediaSettings(AzureMediaIntelligenceOptions options, string samplePath, string mimeType, string extension)
        : base(samplePath, mimeType, extension)
    {
        Options = options;
    }

    public AzureMediaIntelligenceOptions Options { get; }

    public static MediaSettings? TryCreate(JsonElement? root, Func<string?, string, string?> sampleResolver)
    {
        var section = AzureIntegrationConfigurationReader.TryGetVisionSection(root, "AzureVideoIndexer");

        var accountId = AzureIntegrationConfigurationReader.GetString(section, "AccountId");
        var location = AzureIntegrationConfigurationReader.GetString(section, "Location");
        var subscriptionId = AzureIntegrationConfigurationReader.GetString(section, "SubscriptionId");
        var resourceGroup = AzureIntegrationConfigurationReader.GetString(section, "ResourceGroup");
        var accountName = AzureIntegrationConfigurationReader.GetString(section, "AccountName");
        var resourceId = AzureIntegrationConfigurationReader.GetString(section, "ResourceId");
        var armToken = AzureIntegrationConfigurationReader.GetString(section, "ArmAccessToken");
        var pollingInterval = ParsePositiveTimeSpan("PollingInterval", AzureIntegrationConfigurationReader.GetString(section, "PollingInterval"), TimeSpan.FromSeconds(10));
        var maxProcessingTime = ParsePositiveTimeSpan("MaxProcessingTime", AzureIntegrationConfigurationReader.GetString(section, "MaxProcessingTime"), TimeSpan.FromMinutes(15));

        if (!string.IsNullOrWhiteSpace(resourceId) && !resourceId.StartsWith('/'))
        {
            resourceId = "/" + resourceId.TrimStart('/');
        }

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            var parts = AzureIntegrationResourceParser.FromResourceId(resourceId);
            subscriptionId ??= parts.SubscriptionId;
            resourceGroup ??= parts.ResourceGroup;
            accountName ??= parts.AccountName;
        }

        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = accountId,
            Location = location,
            ResourceId = resourceId,
            SubscriptionId = subscriptionId,
            ResourceGroup = resourceGroup,
            AccountName = accountName,
            ArmAccessToken = armToken,
            PollingInterval = pollingInterval,
            MaxProcessingTime = maxProcessingTime
        };

        var sampleSource = Environment.GetEnvironmentVariable(VideoSampleVariable)
            ?? Environment.GetEnvironmentVariable(LegacyVideoSampleVariable);
        var samplePath = sampleResolver(sampleSource, AzureIntegrationDefaults.DefaultMediaSample);

        if (string.IsNullOrWhiteSpace(samplePath))
        {
            return null;
        }

        if (!TryResolve(samplePath, out var mimeType, out var extension))
        {
            return null;
        }

        return new MediaSettings(options, samplePath, mimeType, extension);
    }

    private static TimeSpan ParsePositiveTimeSpan(string settingName, string? value, TimeSpan fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!TimeSpan.TryParse(value, out var parsed))
        {
            throw new InvalidOperationException($"Azure Video Indexer setting '{settingName}' must be a valid TimeSpan value.");
        }

        if (parsed <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"Azure Video Indexer setting '{settingName}' must be greater than zero.");
        }

        return parsed;
    }
}

/// <summary>
/// Azure OpenAI language model configuration snapshot.
/// </summary>
public sealed class LanguageModelsSettings
{
    private const string EndpointEnv = "AZURE_OPENAI_ENDPOINT";
    private const string KeyEnv = "AZURE_OPENAI_KEY";
    private const string ChatDeploymentEnv = "AZURE_OPENAI_CHAT_DEPLOYMENT";
    private const string SpeechDeploymentEnv = "AZURE_OPENAI_SPEECH_DEPLOYMENT";
    private const string ImageSampleEnv = "AZURE_OPENAI_IMAGE_SAMPLE_ASSET";
    private const string AudioSampleEnv = "AZURE_OPENAI_AUDIO_SAMPLE_ASSET";
    private const string SpeechLanguageEnv = "AZURE_OPENAI_SPEECH_LANGUAGE";

    private LanguageModelsSettings(
        string endpoint,
        string apiKey,
        string? chatDeployment,
        string? speechDeployment,
        string? speechLanguage,
        string? imageSamplePath,
        string? imageSampleMimeType,
        string? audioSamplePath,
        string? audioSampleMimeType)
    {
        Endpoint = endpoint;
        ApiKey = apiKey;
        ChatDeployment = chatDeployment;
        SpeechDeployment = speechDeployment;
        SpeechLanguage = speechLanguage;
        ImageSamplePath = imageSamplePath;
        ImageSampleMimeType = imageSampleMimeType;
        AudioSamplePath = audioSamplePath;
        AudioSampleMimeType = audioSampleMimeType;
    }

    public string Endpoint { get; }

    public string ApiKey { get; }

    public string? ChatDeployment { get; }

    public string? SpeechDeployment { get; }

    public string? SpeechModelId => SpeechDeployment;

    public string? SpeechLanguage { get; }

    public string? ImageSamplePath { get; }

    public string? ImageSampleMimeType { get; }

    public string? AudioSamplePath { get; }

    public string? AudioSampleMimeType { get; }

    public bool HasChat => !string.IsNullOrWhiteSpace(ChatDeployment);

    public bool HasSpeech => !string.IsNullOrWhiteSpace(SpeechDeployment);

    public static LanguageModelsSettings? TryCreate(JsonElement? root, Func<string?, string, string?> sampleResolver)
    {
        var section = AzureIntegrationConfigurationReader.TryGetLanguageModelsSection(root);
        var azureSection = AzureIntegrationConfigurationReader.TryGetChild(section, "AzureOpenAI");

        var endpoint = AzureIntegrationConfigurationReader.GetString(azureSection, nameof(Endpoint));
        var apiKey = AzureIntegrationConfigurationReader.GetString(azureSection, nameof(ApiKey));
        var chatDeployment = AzureIntegrationConfigurationReader.GetString(azureSection, nameof(ChatDeployment));
        var speechDeployment = AzureIntegrationConfigurationReader.GetString(azureSection, nameof(SpeechDeployment));
        var imageSample = AzureIntegrationConfigurationReader.GetString(azureSection, "ImageSample");
        var audioSample = AzureIntegrationConfigurationReader.GetString(azureSection, "AudioSample");
        var speechLanguage = AzureIntegrationConfigurationReader.GetString(azureSection, nameof(SpeechLanguage));

        endpoint = OverrideFromEnvironment(EndpointEnv, endpoint);
        apiKey = OverrideFromEnvironment(KeyEnv, apiKey);
        chatDeployment = OverrideFromEnvironment(ChatDeploymentEnv, chatDeployment);
        speechDeployment = OverrideFromEnvironment(SpeechDeploymentEnv, speechDeployment);
        imageSample = OverrideFromEnvironment(ImageSampleEnv, imageSample);
        audioSample = OverrideFromEnvironment(AudioSampleEnv, audioSample);
        speechLanguage = OverrideFromEnvironment(SpeechLanguageEnv, speechLanguage) ?? "en-US";

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var imageSamplePath = sampleResolver(imageSample, AzureIntegrationDefaults.DefaultImageSample);
        var audioSamplePath = sampleResolver(audioSample, AzureIntegrationDefaults.DefaultAudioSample);

        string? imageMimeType = null;
        if (!string.IsNullOrWhiteSpace(imageSamplePath))
        {
            imageMimeType = MimeHelper.GetMimeType(Path.GetExtension(imageSamplePath));
            imageMimeType ??= "image/png";
        }

        string? audioMimeType = null;
        if (!string.IsNullOrWhiteSpace(audioSamplePath))
        {
            audioMimeType = MimeHelper.GetMimeType(Path.GetExtension(audioSamplePath));
            audioMimeType ??= "audio/wav";
        }

        return new LanguageModelsSettings(
            endpoint,
            apiKey,
            chatDeployment,
            speechDeployment,
            speechLanguage,
            imageSamplePath,
            imageMimeType,
            audioSamplePath,
            audioMimeType);
    }

    private static string? OverrideFromEnvironment(string variableName, string? fallback)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}

/// <summary>
/// Helpers to materialize Azure integration configuration payloads.
/// </summary>
public static class AzureIntegrationConfigurationSource
{
    private const string ConfigJsonVariable = "AZURE_INTELLIGENCE_CONFIG_JSON";
    private const string ConfigPathVariable = "AZURE_INTELLIGENCE_CONFIG_PATH";

    public static JsonElement? LoadRootElement(AzureIntegrationConfigurationOptions options)
    {
        var json = TryGetConfigurationJson(options);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string? TryGetConfigurationJson(AzureIntegrationConfigurationOptions options)
    {
        var inline = Environment.GetEnvironmentVariable(ConfigJsonVariable);
        if (!string.IsNullOrWhiteSpace(inline))
        {
            return inline;
        }

        var path = Environment.GetEnvironmentVariable(ConfigPathVariable);
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        return options.DefaultConfigurationFactory?.Invoke();
    }
}

/// <summary>
/// Utilities for working with the Azure integration JSON schema.
/// </summary>
public static class AzureIntegrationConfigurationReader
{
    public static JsonElement? TryGetVisionSection(JsonElement? root, string sectionName)
    {
        if (!root.HasValue)
        {
            return null;
        }

        if (!TryGetProperty(root.Value, "Integrations", out var integrations))
        {
            return null;
        }

        if (!TryGetProperty(integrations, "Vision", out var vision))
        {
            return null;
        }

        return TryGetProperty(vision, sectionName, out var section) ? section : null;
    }

    public static JsonElement? TryGetLanguageModelsSection(JsonElement? root)
    {
        if (!root.HasValue)
        {
            return null;
        }

        if (!TryGetProperty(root.Value, "Integrations", out var integrations))
        {
            return null;
        }

        return TryGetProperty(integrations, "LanguageModels", out var languageModels) ? languageModels : null;
    }

    public static JsonElement? TryGetChild(JsonElement? element, string propertyName)
    {
        if (!element.HasValue)
        {
            return null;
        }

        if (element.Value.ValueKind == JsonValueKind.Object && element.Value.TryGetProperty(propertyName, out var property))
        {
            return property;
        }

        return null;
    }

    public static string? GetString(JsonElement? element, string propertyName)
    {
        if (!element.HasValue)
        {
            return null;
        }

        if (element.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return element.Value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out property))
        {
            return true;
        }

        property = default;
        return false;
    }
}

/// <summary>
/// Helper to parse Azure resource identifiers.
/// </summary>
public static class AzureIntegrationResourceParser
{
    public static (string? SubscriptionId, string? ResourceGroup, string? AccountName) FromResourceId(string? resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return (null, null, null);
        }

        var trimmed = resourceId.Trim('/');
        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);

        string? subscriptionId = null;
        string? resourceGroup = null;
        string? accountName = null;

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Equals("subscriptions", StringComparison.OrdinalIgnoreCase) && i + 1 < segments.Length)
            {
                subscriptionId ??= segments[i + 1];
            }
            else if (segment.Equals("resourcegroups", StringComparison.OrdinalIgnoreCase) && i + 1 < segments.Length)
            {
                resourceGroup ??= segments[i + 1];
            }
            else if (segment.Equals("accounts", StringComparison.OrdinalIgnoreCase) && i + 1 < segments.Length)
            {
                accountName ??= segments[i + 1];
            }
        }

        return (subscriptionId, resourceGroup, accountName);
    }
}
