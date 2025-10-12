using System;
using System.Text;
using MarkItDown.Intelligence.Providers.Aws;
using MarkItDown.Intelligence.Providers.Azure;
using MarkItDown.Intelligence.Providers.Google;
using Spectre.Console;

namespace MarkItDown.Cli;

internal sealed class CliOptionState
{
    private readonly AzureSettings azure = new();
    private readonly GoogleSettings google = new();
    private readonly AwsSettings aws = new();

    public SegmentOptions SegmentOptions { get; set; } = SegmentOptions.Default;

    public string AzureSettingsDescription => azure.Describe();

    public string GoogleSettingsDescription => google.Describe();

    public string AwsSettingsDescription => aws.Describe();

    public MarkItDownOptions BuildOptions()
    {
        return new MarkItDownOptions
        {
            Segments = SegmentOptions,
            AzureIntelligence = azure.ToOptions(),
            GoogleIntelligence = google.ToOptions(),
            AwsIntelligence = aws.ToOptions()
        };
    }

    public void ClearProviders()
    {
        azure.Clear();
        google.Clear();
        aws.Clear();
    }

    public void ConfigureAzure()
    {
        azure.Configure();
    }

    public void ConfigureGoogle()
    {
        google.Configure();
    }

    public void ConfigureAws()
    {
        aws.Configure();
    }

    private abstract class ProviderSettings<TOptions>
    {
        public abstract TOptions? ToOptions();

        public abstract string Describe();

        public abstract void Configure();

        public abstract void Clear();

        protected static string? PromptValue(string field, string? current)
        {
            var label = current is null ? $"{field} [grey](leave blank to skip)[/]" : $"{field} [grey](current: {current}, '-' to clear)[/]";
            var prompt = new TextPrompt<string>(label)
                .AllowEmpty()
                .DefaultValue(current ?? string.Empty);
            var response = AnsiConsole.Prompt(prompt);
            if (string.IsNullOrWhiteSpace(response))
            {
                return current;
            }

            return response == "-" ? null : response;
        }
    }

    private sealed class AzureSettings : ProviderSettings<AzureIntelligenceOptions>
    {
        private string? documentEndpoint;
        private string? documentApiKey;
        private string? documentModelId = "prebuilt-layout";
        private string? visionEndpoint;
        private string? visionApiKey;
        private string? mediaAccountId;
        private string? mediaAccountName;
        private string? mediaLocation;
        private string? mediaSubscriptionId;
        private string? mediaResourceGroup;
        private string? mediaArmToken;
        private string? mediaResourceId;

        public override void Configure()
        {
            documentEndpoint = PromptValue("Document Intelligence endpoint", documentEndpoint);
            documentApiKey = PromptValue("Document Intelligence API key", documentApiKey);
            documentModelId = PromptValue("Document model id", documentModelId) ?? documentModelId;

            visionEndpoint = PromptValue("Vision endpoint", visionEndpoint);
            visionApiKey = PromptValue("Vision API key", visionApiKey);

            mediaAccountId = PromptValue("Video Indexer account id", mediaAccountId);
            mediaAccountName = PromptValue("Video Indexer account name", mediaAccountName);
            mediaLocation = PromptValue("Video Indexer location", mediaLocation);
            mediaSubscriptionId = PromptValue("Video Indexer subscription id", mediaSubscriptionId);
            mediaResourceGroup = PromptValue("Video Indexer resource group", mediaResourceGroup);
            mediaResourceId = PromptValue("Video Indexer resource id", mediaResourceId);
            mediaArmToken = PromptValue("Video Indexer ARM token", mediaArmToken);
        }

        public override string Describe()
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(documentEndpoint))
            {
                builder.Append("Document endpoint");
            }
            if (!string.IsNullOrWhiteSpace(visionEndpoint))
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }
                builder.Append("Vision endpoint");
            }
            if (!string.IsNullOrWhiteSpace(mediaAccountId))
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }
                builder.Append("Video Indexer");
            }

            return builder.ToString();
        }

        public override AzureIntelligenceOptions? ToOptions()
        {
            if (string.IsNullOrWhiteSpace(documentEndpoint) &&
                string.IsNullOrWhiteSpace(visionEndpoint) &&
                (string.IsNullOrWhiteSpace(mediaAccountId) || string.IsNullOrWhiteSpace(mediaLocation)))
            {
                return null;
            }

            return new AzureIntelligenceOptions
            {
                DocumentIntelligence = string.IsNullOrWhiteSpace(documentEndpoint)
                    ? null
                    : new AzureDocumentIntelligenceOptions
                    {
                        Endpoint = documentEndpoint,
                        ApiKey = documentApiKey,
                        ModelId = string.IsNullOrWhiteSpace(documentModelId) ? "prebuilt-layout" : documentModelId
                    },
                Vision = string.IsNullOrWhiteSpace(visionEndpoint)
                    ? null
                    : new AzureVisionOptions
                    {
                        Endpoint = visionEndpoint,
                        ApiKey = visionApiKey
                    },
                Media = string.IsNullOrWhiteSpace(mediaAccountId) || string.IsNullOrWhiteSpace(mediaLocation)
                    ? null
                    : new AzureMediaIntelligenceOptions
                    {
                        AccountId = mediaAccountId,
                        AccountName = mediaAccountName,
                        Location = mediaLocation,
                        SubscriptionId = mediaSubscriptionId,
                        ResourceGroup = mediaResourceGroup,
                        ResourceId = mediaResourceId,
                        ArmAccessToken = mediaArmToken
                    }
            };
        }

        public override void Clear()
        {
            documentEndpoint = null;
            documentApiKey = null;
            documentModelId = "prebuilt-layout";
            visionEndpoint = null;
            visionApiKey = null;
            mediaAccountId = null;
            mediaAccountName = null;
            mediaLocation = null;
            mediaSubscriptionId = null;
            mediaResourceGroup = null;
            mediaResourceId = null;
            mediaArmToken = null;
        }
    }

    private sealed class GoogleSettings : ProviderSettings<GoogleIntelligenceOptions>
    {
        private string? projectId;
        private string? location;
        private string? processorId;
        private string? docCredentialsPath;
        private string? docJsonCredentials;
        private string? visionCredentialsPath;
        private string? visionJsonCredentials;
        private string? mediaCredentialsPath;
        private string? mediaJsonCredentials;
        private string languageCode = "en-US";

        public override void Configure()
        {
            projectId = PromptValue("Project id", projectId);
            location = PromptValue("Location (e.g. us)", location);
            processorId = PromptValue("Processor id", processorId);
            docCredentialsPath = PromptValue("Document credentials path", docCredentialsPath);
            docJsonCredentials = PromptValue("Document inline JSON credentials", docJsonCredentials);
            visionCredentialsPath = PromptValue("Vision credentials path", visionCredentialsPath);
            visionJsonCredentials = PromptValue("Vision inline JSON credentials", visionJsonCredentials);
            mediaCredentialsPath = PromptValue("Speech credentials path", mediaCredentialsPath);
            mediaJsonCredentials = PromptValue("Speech inline JSON credentials", mediaJsonCredentials);
            languageCode = PromptValue("Speech language code", languageCode) ?? languageCode;
        }

        public override string Describe()
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(projectId))
            {
                builder.Append(projectId);
            }
            if (!string.IsNullOrWhiteSpace(location))
            {
                builder.Append(builder.Length > 0 ? $" ({location})" : location);
            }
            return builder.ToString();
        }

        public override GoogleIntelligenceOptions? ToOptions()
        {
            if (string.IsNullOrWhiteSpace(projectId) && string.IsNullOrWhiteSpace(processorId) &&
                string.IsNullOrWhiteSpace(docCredentialsPath) && string.IsNullOrWhiteSpace(docJsonCredentials) &&
                string.IsNullOrWhiteSpace(visionCredentialsPath) && string.IsNullOrWhiteSpace(visionJsonCredentials) &&
                string.IsNullOrWhiteSpace(mediaCredentialsPath) && string.IsNullOrWhiteSpace(mediaJsonCredentials))
            {
                return null;
            }

            return new GoogleIntelligenceOptions
            {
                DocumentIntelligence = string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(processorId)
                    ? null
                    : new GoogleDocumentIntelligenceOptions
                    {
                        ProjectId = projectId,
                        Location = string.IsNullOrWhiteSpace(location) ? "us" : location,
                        ProcessorId = processorId,
                        CredentialsPath = docCredentialsPath,
                        JsonCredentials = docJsonCredentials
                    },
                Vision = new GoogleVisionOptions
                {
                    CredentialsPath = visionCredentialsPath,
                    JsonCredentials = visionJsonCredentials
                },
                Media = new GoogleMediaIntelligenceOptions
                {
                    CredentialsPath = mediaCredentialsPath,
                    JsonCredentials = mediaJsonCredentials,
                    LanguageCode = string.IsNullOrWhiteSpace(languageCode) ? "en-US" : languageCode
                }
            };
        }

        public override void Clear()
        {
            projectId = null;
            location = null;
            processorId = null;
            docCredentialsPath = null;
            docJsonCredentials = null;
            visionCredentialsPath = null;
            visionJsonCredentials = null;
            mediaCredentialsPath = null;
            mediaJsonCredentials = null;
            languageCode = "en-US";
        }
    }

    private sealed class AwsSettings : ProviderSettings<AwsIntelligenceOptions>
    {
        private string? accessKeyId;
        private string? secretAccessKey;
        private string? sessionToken;
        private string? region;
        private string? textractRegion;
        private string? rekognitionRegion;
        private string? transcribeRegion;
        private string? transcribeInputBucket;
        private string? transcribeOutputBucket;

        public override void Configure()
        {
            accessKeyId = PromptValue("Access key id", accessKeyId);
            secretAccessKey = PromptValue("Secret access key", secretAccessKey);
            sessionToken = PromptValue("Session token", sessionToken);
            region = PromptValue("Default region", region);
            textractRegion = PromptValue("Textract region override", textractRegion);
            rekognitionRegion = PromptValue("Rekognition region override", rekognitionRegion);
            transcribeRegion = PromptValue("Transcribe region override", transcribeRegion);
            transcribeInputBucket = PromptValue("Transcribe input bucket", transcribeInputBucket);
            transcribeOutputBucket = PromptValue("Transcribe output bucket", transcribeOutputBucket);
        }

        public override string Describe()
        {
            if (string.IsNullOrWhiteSpace(accessKeyId) && string.IsNullOrWhiteSpace(region) && string.IsNullOrWhiteSpace(transcribeInputBucket))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(region))
            {
                builder.Append(region);
            }
            if (!string.IsNullOrWhiteSpace(transcribeInputBucket))
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append($"Transcribe bucket: {transcribeInputBucket}");
            }

            return builder.ToString();
        }

        public override AwsIntelligenceOptions? ToOptions()
        {
            if (string.IsNullOrWhiteSpace(accessKeyId) && string.IsNullOrWhiteSpace(secretAccessKey) &&
                string.IsNullOrWhiteSpace(transcribeInputBucket) && string.IsNullOrWhiteSpace(region))
            {
                return null;
            }

            return new AwsIntelligenceOptions
            {
                DocumentIntelligence = new AwsDocumentIntelligenceOptions
                {
                    AccessKeyId = accessKeyId,
                    SecretAccessKey = secretAccessKey,
                    SessionToken = sessionToken,
                    Region = textractRegion ?? region
                },
                Vision = new AwsVisionOptions
                {
                    AccessKeyId = accessKeyId,
                    SecretAccessKey = secretAccessKey,
                    SessionToken = sessionToken,
                    Region = rekognitionRegion ?? region
                },
                Media = string.IsNullOrWhiteSpace(transcribeInputBucket)
                    ? null
                    : new AwsMediaIntelligenceOptions
                    {
                        AccessKeyId = accessKeyId,
                        SecretAccessKey = secretAccessKey,
                        SessionToken = sessionToken,
                        Region = transcribeRegion ?? region,
                        InputBucketName = transcribeInputBucket,
                        OutputBucketName = transcribeOutputBucket
                    }
            };
        }

        public override void Clear()
        {
            accessKeyId = null;
            secretAccessKey = null;
            sessionToken = null;
            region = null;
            textractRegion = null;
            rekognitionRegion = null;
            transcribeRegion = null;
            transcribeInputBucket = null;
            transcribeOutputBucket = null;
        }
    }
}
