using System.Net;
using System.Net.Http;
using System.Text;
using ManagedCode.Storage.FileSystem;
using ManagedCode.Storage.FileSystem.Options;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using MarkItDown.Intelligence.Providers.Azure;
using Shouldly;

namespace MarkItDown.Tests.Intelligence;

public class AzureMediaTranscriptionProviderTests
{
    private sealed class StubArmTokenService : ArmTokenService
    {
        private readonly string? token;

        public StubArmTokenService(string? token)
            : base(token)
        {
            this.token = token;
        }

        public override Task<string?> GetArmTokenAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(token);
        }
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses = new();

        public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            responses.Enqueue(factory);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (responses.Count == 0)
            {
                throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
            }

            var response = responses.Dequeue()(request);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task TranscribeAsync_ReturnsSegmentsFromVideoIndexer()
    {
        var handler = new SequenceHandler();

        handler.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri!.Host.ShouldBe("management.azure.com");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"accessToken\": \"token123\", \"expirationTime\": \"2025-01-01T00:00:00Z\" }", Encoding.UTF8, "application/json")
            };
        });

        handler.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"id\": \"video123\" }", Encoding.UTF8, "application/json")
            };
        });

        handler.Enqueue(request =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"state\": \"Processed\" }", Encoding.UTF8, "application/json")
            };
        });

        handler.Enqueue(request =>
        {
            var transcriptJson = "{" +
                "\"state\":\"Processed\"," +
                "\"videos\":[{" +
                "\"id\":\"index-123\"," +
                "\"processingProgress\":\"100%\"," +
                "\"insights\":{" +
                "\"language\":\"en-US\"," +
                "\"duration\":\"0:00:10\"," +
                "\"speakers\":[{\"id\":1,\"name\":\"Speaker #1\"}]," +
                "\"sentiments\":[{\"sentimentType\":\"Neutral\",\"averageScore\":0.5,\"instances\":[{\"start\":\"0:00:00\",\"end\":\"0:00:10\"}]}]," +
                "\"topics\":[{\"name\":\"Health\",\"confidence\":0.998,\"instances\":[{\"start\":\"0:00:00\",\"end\":\"0:00:10\"}]}]," +
                "\"keywords\":[{\"text\":\"AGI\",\"confidence\":0.95,\"instances\":[{\"start\":\"0:00:00\",\"end\":\"0:00:10\"}]}]," +
                "\"transcript\":[" +
                "{\"text\":\"Hello world\",\"confidence\":0.72,\"speakerId\":1,\"instances\":[{\"start\":\"0:00:00\",\"end\":\"0:00:05\"}]}," +
                "{\"text\":\"Segment two\",\"confidence\":0.68,\"speakerId\":1,\"instances\":[{\"start\":\"0:00:05\",\"end\":\"0:00:10\"}]}" +
                "]}}]}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(transcriptJson, Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        };

        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "trial",
            SubscriptionId = "sub",
            ResourceGroup = "rg",
            AccountName = "account"
        };

        var provider = new AzureMediaTranscriptionProvider(options, httpClient, new StubArmTokenService("arm-token"));

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "audio/wav", extension: ".wav", fileName: "sample.wav");

        var result = await provider.TranscribeAsync(stream, streamInfo);

        result.ShouldNotBeNull();
        result!.Segments.Count.ShouldBe(2);
        result.Segments[0].Text.ShouldBe("Hello world");
        result.Segments[0].Start.ShouldBe(TimeSpan.Zero);
        result.Segments[0].End.ShouldBe(TimeSpan.FromSeconds(5));
        result.Segments[0].Metadata[MetadataKeys.Speaker].ShouldBe("Speaker #1");
        result.Segments[0].Metadata[MetadataKeys.Sentiment].ShouldBe("Neutral");
        result.Segments[0].Metadata[MetadataKeys.Topics].ShouldContain("Health");
        result.Segments[0].Metadata[MetadataKeys.Keywords].ShouldContain("AGI");
        result.Segments[1].Text.ShouldBe("Segment two");
        result.Metadata[MetadataKeys.Provider].ShouldBe(MetadataValues.ProviderAzureVideoIndexer);
        result.Metadata[MetadataKeys.Speakers].ShouldContain("Speaker #1");
        result.Metadata[MetadataKeys.Sentiments].ShouldContain("Neutral");
        result.Metadata[MetadataKeys.VideoIndexerState].ShouldBe("Processed");
        result.Metadata[MetadataKeys.VideoIndexerIndexId].ShouldBe("index-123");
        result.Metadata[MetadataKeys.VideoIndexerProgress].ShouldBe("100%");
        result.Segments[0].Metadata[MetadataKeys.VideoIndexerState].ShouldBe("Processed");
    }

    [Fact]
    public async Task TranscribeAsync_WhenRequestProvidesSourceUrl_UsesVideoUrlUpload()
    {
        var handler = new SequenceHandler();

        handler.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri!.Host.ShouldBe("management.azure.com");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"accessToken\": \"token123\", \"expirationTime\": \"2025-01-01T00:00:00Z\" }", Encoding.UTF8, "application/json")
            };
        });

        handler.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri!.ToString().ShouldContain("/trial/Accounts/account/Videos?");
            request.RequestUri!.Query.ShouldContain("videoUrl=https%3A%2F%2Fstorage.example.com%2Fuploads%2Fclip.mp4%3Fsv%3D1%26sig%3Dabc");
            request.Content.ShouldBeNull();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"id\": \"video123\" }", Encoding.UTF8, "application/json")
            };
        });

        handler.Enqueue(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"state\": \"Processed\" }", Encoding.UTF8, "application/json")
            });

        handler.Enqueue(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{ \"state\":\"Processed\",\"videos\":[{\"insights\":{\"language\":\"en-US\",\"transcript\":[{\"text\":\"Hello world\",\"instances\":[{\"start\":\"0:00:00\",\"end\":\"0:00:02\"}]}]}}]}",
                    Encoding.UTF8,
                    "application/json")
            });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        };

        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "trial",
            SubscriptionId = "sub",
            ResourceGroup = "rg",
            AccountName = "account"
        };

        var provider = new AzureMediaTranscriptionProvider(options, httpClient, new StubArmTokenService("arm-token"));

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(
            mimeType: "video/mp4",
            extension: ".mp4",
            fileName: "sample.mp4",
            url: "https://platform.example.com/watch/abc123");
        var request = new MediaTranscriptionRequest(
            PreferredProvider: MediaTranscriptionProviderKind.Azure,
            Language: "English",
            SourceUrl: "https://storage.example.com/uploads/clip.mp4?sv=1&sig=abc");

        var result = await provider.TranscribeAsync(stream, streamInfo, request);

        result.ShouldNotBeNull();
        result!.Segments.Count.ShouldBe(1);
        result.Segments[0].Text.ShouldBe("Hello world");
    }

    [Fact]
    public async Task TranscribeAsync_WhenRequestSourceUrlIsNotHttp_ThrowsFileConversionException()
    {
        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "trial",
            AccountName = "account",
            SubscriptionId = "sub",
            ResourceGroup = "rg"
        };

        var provider = new AzureMediaTranscriptionProvider(options, new HttpClient(new SequenceHandler())
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        }, new StubArmTokenService("arm-token"));

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "video/mp4", extension: ".mp4", fileName: "sample.mp4");
        var request = new MediaTranscriptionRequest(
            PreferredProvider: MediaTranscriptionProviderKind.Azure,
            Language: "English",
            SourceUrl: "ftp://storage.example.com/uploads/clip.mp4");

        var exception = await Should.ThrowAsync<FileConversionException>(
            () => provider.TranscribeAsync(stream, streamInfo, request));

        exception.Message.ShouldContain("absolute http/https URL");
    }

    [Fact]
    public async Task TranscribeAsync_WhenUploadRouteStream_UsesMultipartUploadEvenWhenUrlsExist()
    {
        var handler = new SequenceHandler();

        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ \"accessToken\": \"token123\", \"expirationTime\": \"2025-01-01T00:00:00Z\" }", Encoding.UTF8, "application/json")
        });

        handler.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri!.ToString().ShouldContain("/trial/Accounts/account/Videos?");
            request.RequestUri!.Query.ShouldNotContain("videoUrl=");
            request.Content.ShouldNotBeNull();
            request.Content.ShouldBeOfType<MultipartFormDataContent>();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"id\": \"video123\" }", Encoding.UTF8, "application/json")
            };
        });

        handler.Enqueue(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"state\": \"Processed\" }", Encoding.UTF8, "application/json")
            });

        handler.Enqueue(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{ \"state\":\"Processed\",\"videos\":[{\"insights\":{\"language\":\"en-US\",\"transcript\":[{\"text\":\"Hello world\",\"instances\":[{\"start\":\"0:00:00\",\"end\":\"0:00:02\"}]}]}}]}",
                    Encoding.UTF8,
                    "application/json")
            });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        };

        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "trial",
            SubscriptionId = "sub",
            ResourceGroup = "rg",
            AccountName = "account"
        };

        var provider = new AzureMediaTranscriptionProvider(options, httpClient, new StubArmTokenService("arm-token"));
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(
            mimeType: "video/mp4",
            extension: ".mp4",
            fileName: "sample.mp4",
            url: "https://platform.example.com/watch/abc123");
        var request = new MediaTranscriptionRequest(
            PreferredProvider: MediaTranscriptionProviderKind.Azure,
            Language: "English",
            SourceUrl: "https://storage.example.com/uploads/clip.mp4?sv=1&sig=abc",
            UploadRoute: MediaUploadRoute.Stream);

        var result = await provider.TranscribeAsync(stream, streamInfo, request);

        result.ShouldNotBeNull();
        result!.Segments.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TranscribeAsync_WhenUploadRouteStorageUrlWithFileSystemStorage_UploadsFileAndFailsForNonHttpUri()
    {
        var handler = new SequenceHandler();

        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ \"accessToken\": \"token123\", \"expirationTime\": \"2025-01-01T00:00:00Z\" }", Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        };

        var storageRoot = Path.Combine(Path.GetTempPath(), $"markitdown-storage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);

        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "trial",
            SubscriptionId = "sub",
            ResourceGroup = "rg",
            AccountName = "account",
            UploadStorageFactory = () => new FileSystemStorage(new FileSystemStorageOptions
            {
                BaseFolder = storageRoot,
                CreateContainerIfNotExists = true
            }),
            UploadStorageDirectoryResolver = _ => "uploads"
        };

        var provider = new AzureMediaTranscriptionProvider(options, httpClient, new StubArmTokenService("arm-token"));
        var path = Path.Combine(Path.GetTempPath(), $"markitdown-video-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);

        try
        {
            await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            var streamInfo = new StreamInfo(
                mimeType: "video/mp4",
                extension: ".mp4",
                fileName: "sample.mp4",
                localPath: path);
            var request = new MediaTranscriptionRequest(
                PreferredProvider: MediaTranscriptionProviderKind.Azure,
                Language: "English",
                UploadRoute: MediaUploadRoute.StorageUrl);

            var exception = await Should.ThrowAsync<FileConversionException>(
                () => provider.TranscribeAsync(stream, streamInfo, request));

            exception.Message.ShouldContain("Storage upload result URI must be an absolute http/https URL");

            var uploadedFiles = Directory.GetFiles(storageRoot, "*.mp4", SearchOption.AllDirectories);
            uploadedFiles.Length.ShouldBe(1);
            var uploadedFileInfo = new FileInfo(uploadedFiles[0]);
            uploadedFileInfo.Exists.ShouldBeTrue();
            uploadedFileInfo.Length.ShouldBe(4);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TranscribeAsync_WhenUploadRouteStorageUrlWithoutProvider_ThrowsFileConversionException()
    {
        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "trial",
            AccountName = "account",
            SubscriptionId = "sub",
            ResourceGroup = "rg"
        };

        var provider = new AzureMediaTranscriptionProvider(options, new HttpClient(new SequenceHandler())
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        }, new StubArmTokenService("arm-token"));

        var path = Path.Combine(Path.GetTempPath(), $"markitdown-video-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);

        try
        {
            await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            var streamInfo = new StreamInfo(mimeType: "video/mp4", extension: ".mp4", fileName: "sample.mp4", localPath: path);
            var request = new MediaTranscriptionRequest(
                PreferredProvider: MediaTranscriptionProviderKind.Azure,
                Language: "English",
                UploadRoute: MediaUploadRoute.StorageUrl);

            var exception = await Should.ThrowAsync<FileConversionException>(
                () => provider.TranscribeAsync(stream, streamInfo, request));

            exception.Message.ShouldContain("UploadStorageFactory");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task TranscribeAsync_ReturnsNullWhenTokenUnavailable()
    {
        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "trial",
            AccountName = "account",
            SubscriptionId = "sub",
            ResourceGroup = "rg"
        };

        var provider = new AzureMediaTranscriptionProvider(options, new HttpClient(new SequenceHandler())
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        }, new StubArmTokenService(null));

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "audio/wav", extension: ".wav");

        var result = await provider.TranscribeAsync(stream, streamInfo);
        result.ShouldBeNull();
    }
}
