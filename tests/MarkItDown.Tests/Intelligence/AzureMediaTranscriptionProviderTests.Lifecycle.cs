using System.Net;
using System.Security.Cryptography;
using System.Text;
using MarkItDown.Intelligence.Providers.Azure;
using Shouldly;

namespace MarkItDown.Tests.Intelligence;

public partial class AzureMediaTranscriptionProviderTests
{
    private const string RecordingExternalId = "company_command_12345";
    private const string RecordingVideoId = "recording123";
    private const string VideosPath = "/westeurope/Accounts/account/Videos";

    [Fact]
    public async Task Lifecycle_RecoversTheSameUploadAndReturnsOriginalTimedSpeech()
    {
        var sequence = new SequenceHandler();
        var filePath = TestAssetLoader.GetAssetPath(TestAssetCatalog.MeetingAudioWav);
        await using var stream = File.OpenRead(filePath);
        var sourceHash = await SHA256.HashDataAsync(stream);
        AddLifecycleAuthentication(sequence);
        sequence.Enqueue(async request =>
        {
            AssertLifecycleRequest(request, HttpMethod.Post, VideosPath);
            var query = request.RequestUri!.Query;
            query.ShouldContain($"externalId={RecordingExternalId}");
            query.ShouldContain("language=fr-FR");
            query.ShouldContain("privacy=private");
            query.ShouldContain("indexingPreset=BasicAudio");
            query.ShouldContain("streamingPreset=NoStreaming");
            query.ShouldContain("retentionPeriod=1");
            query.ShouldNotContain("videoUrl=");
            query.ShouldNotContain("original-recording");
            var upload = Assert.IsType<MultipartFormDataContent>(request.Content).Single();
            upload.Headers.ContentDisposition!.Name!.Trim('"').ShouldBe("fileName");
            upload.Headers.ContentDisposition!.FileName!.Trim('"').ShouldBe(RecordingExternalId + ".wav");
            upload.Headers.ContentType!.MediaType.ShouldBe("audio/wav");
            await using var sent = await upload.ReadAsStreamAsync();
            (await SHA256.HashDataAsync(sent)).SequenceEqual(sourceHash).ShouldBeTrue();
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });
        sequence.Enqueue(request =>
        {
            AssertLifecycleRequest(request, HttpMethod.Get, VideosPath + "/Search");
            request.RequestUri!.Query.ShouldContain($"externalId={RecordingExternalId}");
            return LifecycleJson("""{"results":[]}""");
        });
        sequence.Enqueue(request =>
        {
            AssertLifecycleRequest(request, HttpMethod.Get, VideosPath + "/Search");
            request.RequestUri!.Query.ShouldContain($"externalId={RecordingExternalId}");
            return LifecycleJson(
                $$"""{"results":[{"id":"{{RecordingVideoId}}","externalId":"{{RecordingExternalId}}"}]}""");
        });
        sequence.Enqueue(request =>
        {
            AssertLifecycleRequest(request, HttpMethod.Get, VideosPath + $"/{RecordingVideoId}/Index");
            return LifecycleJson("""{"state":"Processing"}""");
        });
        sequence.Enqueue(request =>
        {
            AssertLifecycleRequest(request, HttpMethod.Get, VideosPath + $"/{RecordingVideoId}/Index");
            request.RequestUri!.Query.ShouldNotContain("language=");
            return LifecycleJson("""
                {
                  "state": "Processed",
                  "videos": [{"insights": {
                    "language": "fr-FR",
                    "transcript": [{
                      "text": "Le premier contrôle est à onze heures.",
                      "instances": [{"start": "0:00:01.125", "end": "0:00:03.5"}]
                    }]
                  }}]
                }
                """);
        });
        sequence.Enqueue(request =>
        {
            AssertLifecycleRequest(request, HttpMethod.Delete, VideosPath + $"/{RecordingVideoId}");
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        sequence.Enqueue(request =>
        {
            AssertLifecycleRequest(request, HttpMethod.Delete, VideosPath + $"/{RecordingVideoId}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = CreateLifecycleHttpClient(sequence);
        using var provider = CreateLifecycleProvider(client);
        var input = new StreamInfo(mimeType: "audio/wav", extension: ".wav", fileName: "original-recording.wav",
            url: "https://example.invalid/ignored");

        var ambiguous = await Should.ThrowAsync<HttpRequestException>(
            () => provider.SubmitAsync(stream, input, RecordingExternalId, "fr-FR"));
        ambiguous.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        stream.CanRead.ShouldBeTrue();
        (await provider.FindVideoIdAsync(RecordingExternalId)).ShouldBeNull();
        var recoveredId = await provider.FindVideoIdAsync(RecordingExternalId);
        recoveredId.ShouldBe(RecordingVideoId);
        var pending = await provider.ReadAsync(recoveredId!);
        pending.State.ShouldBe(AzureVideoIndexerTranscriptionState.Pending);
        pending.Transcript.ShouldBeNull();
        var completed = await provider.ReadAsync(recoveredId!);
        completed.State.ShouldBe(AzureVideoIndexerTranscriptionState.Processed);
        completed.Transcript.ShouldNotBeNull();
        completed.Transcript!.Language.ShouldBe("fr-FR");
        var segment = completed.Transcript.Segments.Single();
        segment.Text.ShouldBe("Le premier contrôle est à onze heures.");
        segment.Start.ShouldBe(TimeSpan.FromMilliseconds(1125));
        segment.End.ShouldBe(TimeSpan.FromMilliseconds(3500));
        await provider.DeleteAsync(recoveredId!);
        await provider.DeleteAsync(recoveredId!);
    }

    [Fact]
    public async Task SubmitAsync_ReturnsTheAcknowledgedOpaqueIdWithoutWaitingForProcessing()
    {
        var sequence = new SequenceHandler();
        AddLifecycleAuthentication(sequence);
        sequence.Enqueue(request =>
        {
            AssertLifecycleRequest(request, HttpMethod.Post, VideosPath);
            return LifecycleJson($$"""{"id":"{{RecordingVideoId}}"}""");
        });
        using var client = CreateLifecycleHttpClient(sequence);
        using var provider = CreateLifecycleProvider(client);
        await using var input = File.OpenRead(TestAssetLoader.GetAssetPath(TestAssetCatalog.VideoIndexerTestVideoMp4));

        var videoId = await provider.SubmitAsync(input, new StreamInfo(mimeType: "video/mp4", extension: ".mp4"),
            RecordingExternalId, "uk-UA");

        videoId.ShouldBe(RecordingVideoId);
    }

    [Theory]
    [InlineData("""{"results":[{"id":"otherRecording","externalId":"otherCommand"}]}""")]
    [InlineData("""{"results":[{"id":"first","externalId":"company_command_12345"},{"id":"second","externalId":"company_command_12345"}]}""")]
    public async Task FindVideoIdAsync_WhenSearchCannotProveOneExactOwner_FailsWithoutReturningAnId(string payload)
    {
        var sequence = new SequenceHandler();
        AddLifecycleAuthentication(sequence);
        sequence.Enqueue(request =>
        {
            AssertLifecycleRequest(request, HttpMethod.Get, VideosPath + "/Search");
            return LifecycleJson(payload);
        });
        using var client = CreateLifecycleHttpClient(sequence);
        using var provider = CreateLifecycleProvider(client);

        await Should.ThrowAsync<FileConversionException>(() => provider.FindVideoIdAsync(RecordingExternalId));
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Quarantined")]
    public async Task ReadAsync_WhenProviderCannotProcessTheRecording_ReturnsFailedWithoutTranscript(string state)
    {
        var sequence = new SequenceHandler();
        AddLifecycleAuthentication(sequence);
        sequence.Enqueue(request =>
        {
            AssertLifecycleRequest(request, HttpMethod.Get, VideosPath + $"/{RecordingVideoId}/Index");
            return LifecycleJson($$$"""{"state":"{{{state}}}","videos":[{"insights":{"transcript":[{"text":"Private data"}]}}]}""");
        });
        using var client = CreateLifecycleHttpClient(sequence);
        using var provider = CreateLifecycleProvider(client);

        var result = await provider.ReadAsync(RecordingVideoId);

        result.State.ShouldBe(AzureVideoIndexerTranscriptionState.Failed);
        result.Transcript.ShouldBeNull();
    }

    private static void AddLifecycleAuthentication(SequenceHandler sequence) => sequence.Enqueue(request =>
    {
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.Host.ShouldBe("management.azure.com");
        request.Headers.Authorization!.Parameter.ShouldBe("arm-token");
        return LifecycleJson("""{"accessToken":"video-access-token"}""");
    });

    private static void AssertLifecycleRequest(HttpRequestMessage request, HttpMethod method, string path)
    {
        request.Method.ShouldBe(method);
        request.RequestUri!.Host.ShouldBe("api.videoindexer.ai");
        request.RequestUri.AbsolutePath.ShouldBe(path);
        request.RequestUri.Query.ShouldNotContain("accessToken");
        request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        request.Headers.Authorization.Parameter.ShouldBe("video-access-token");
    }

    private static HttpClient CreateLifecycleHttpClient(SequenceHandler sequence) =>
        new(sequence) { BaseAddress = new Uri("https://api.videoindexer.ai/") };

    private static AzureMediaTranscriptionProvider CreateLifecycleProvider(HttpClient client) =>
        new(new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "westeurope",
            ResourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.VideoIndexer/accounts/account"
        }, client, new StubArmTokenService("arm-token"));

    private static HttpResponseMessage LifecycleJson(string payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
}
