using System;
using System.Threading;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Configuration;
using MarkItDown.Intelligence.Providers.Azure;
using MarkItDown.Tests.Manual;

namespace MarkItDown.Tests.Intelligence.Integration;

public sealed class AzureIntelligenceFixture
{
    public const string DocumentSkipMessage = "Provide Azure Document Intelligence credentials via environment variables or azure-intelligence-config.json to run this test.";
    public const string VisionSkipMessage = "Provide Azure Vision credentials via environment variables or azure-intelligence-config.json to run this test.";
    public const string MediaSkipMessage = "Provide Azure Video Indexer credentials via environment variables or azure-intelligence-config.json to run this test.";
    public const string LanguageModelsSkipMessage = "Provide Azure OpenAI credentials via environment variables or azure-intelligence-config.json to run language model tests.";

    private readonly AzureIntegrationSettings settings = AzureIntegrationConfigurationFactory.Load();

    internal DocumentSettings? Document => settings.Document;
    internal VisionSettings? Vision => settings.Vision;
    internal MediaSettings? Media => settings.Media;
    internal LanguageModelsSettings? LanguageModels => settings.LanguageModels;
    internal StaticAiModelProvider? AiModels => AzureIntegrationConfigurationFactory.CreateAiModelProvider(settings.LanguageModels);

    public static CancellationTokenSource CreateDefaultCancellationToken()
    {
        return new CancellationTokenSource(TimeSpan.FromMinutes(5));
    }
}
