using System;
using System.IO;
using System.Reflection;
using Azure;
using Azure.AI.OpenAI;
using Azure.Core;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Configuration;
using MarkItDown.Tests;
using Microsoft.Extensions.AI;
using OpenAI.Audio;
using OpenAI.Chat;

#pragma warning disable MEAI001

namespace MarkItDown.Tests.Manual;

internal static class AzureIntegrationConfigurationFactory
{
    public static AzureIntegrationConfigurationOptions CreateOptions()
    {
        return new AzureIntegrationConfigurationOptions
        {
            SampleResolver = ResolveSample,
            DefaultConfigurationFactory = () => AzureIntegrationConfigDefaults.DefaultJson
        };
    }

    public static AzureIntegrationSettings Load()
        => AzureIntegrationSettings.Load(CreateOptions());

    public static StaticAiModelProvider? CreateAiModelProvider(LanguageModelsSettings? settings)
    {
        if (settings is null)
        {
            return null;
        }

        ChatClient? chatClient = null;
        AudioClient? audioClient = null;

        try
        {
            var client = new AzureOpenAIClient(new Uri(settings.Endpoint), new AzureKeyCredential(settings.ApiKey));

            if (settings.HasChat)
            {
                chatClient = client.GetChatClient(settings.ChatDeployment!);
            }

            if (settings.HasSpeech)
            {
                audioClient = client.GetAudioClient(settings.SpeechDeployment!);
            }
        }
        catch
        {
            return null;
        }

        IChatClient? aiChatClient = chatClient?.AsIChatClient();
        ISpeechToTextClient? aiSpeechClient = audioClient?.AsISpeechToTextClient();

        if (aiChatClient is null && aiSpeechClient is null)
        {
            return null;
        }

        return new StaticAiModelProvider(aiChatClient, aiSpeechClient);
    }

    private static string? ResolveSample(string? value, string defaultAsset)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var resolved = TryResolveCandidate(value);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return TryResolveCandidate(defaultAsset);
    }

    private static string? TryResolveCandidate(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (Path.IsPathRooted(candidate))
        {
            return File.Exists(candidate) ? candidate : null;
        }

        if (TryResolveFromCatalog(candidate, out var catalogPath))
        {
            return catalogPath;
        }

        var assetsDirectory = TestAssetLoader.AssetsDirectory;
        var fromAssets = Path.Combine(assetsDirectory, candidate);
        if (File.Exists(fromAssets))
        {
            return fromAssets;
        }

        return File.Exists(candidate) ? candidate : null;
    }

    private static bool TryResolveFromCatalog(string constantName, out string path)
    {
        var field = typeof(TestAssetCatalog).GetField(constantName, BindingFlags.Public | BindingFlags.Static);
        if (field?.GetValue(null) is string value)
        {
            try
            {
                path = TestAssetLoader.GetAssetPath(value);
                return true;
            }
            catch (FileNotFoundException)
            {
                // fall through
            }
        }

        try
        {
            path = TestAssetLoader.GetAssetPath(constantName);
            return true;
        }
        catch (FileNotFoundException)
        {
            path = string.Empty;
            return false;
        }
    }
}
