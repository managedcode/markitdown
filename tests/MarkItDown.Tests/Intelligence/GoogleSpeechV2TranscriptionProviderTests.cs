using System.Security.Cryptography;
using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Speech.V2;
using Google.LongRunning;
using Google.Protobuf.WellKnownTypes;
using ManagedCode.Storage.FileSystem;
using ManagedCode.Storage.FileSystem.Options;
using MarkItDown.Intelligence.Providers.Google;
using Moq;
using Xunit;

namespace MarkItDown.Tests.Intelligence;

public sealed class GoogleSpeechV2TranscriptionProviderTests
{
    private const string ProjectNumber = "123456789";
    private const string Location = "eu";
    private const string Bucket = "private-company-media";
    private const string Name = $"projects/{ProjectNumber}/locations/{Location}/operations/v2-meeting";
    private const string SourceHash = "aaaabbbbccccddddeeeeffff0000111122223333444455556666777788889999";

    [Fact]
    public async Task StagesOriginalRecordingAndReadsTimedTranscriptAfterProviderIsRecreated()
    {
        var operations = new Mock<OperationsClient>(MockBehavior.Strict);
        var speech = new Mock<SpeechClient>(MockBehavior.Strict);
        speech.SetupGet(client => client.BatchRecognizeOperationsClient).Returns(operations.Object);

        var root = Path.Combine(AppContext.BaseDirectory, "TestResults", "google-staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var source = File.OpenRead(TestAssetLoader.GetAssetPath(TestAssetCatalog.MeetingAudioWav));
            var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(source));
            source.Position = 0;
            var commandId = Guid.NewGuid();
            var media = new StreamInfo(mimeType: "audio/wav", extension: "wav");
            var provider = CreateProvider(speech.Object, root);
            var input = provider.CreateInputReference(commandId, sha256, media);
            await provider.StageAsync(input, source, media);

            var objectPath = $"markitdown-transcription/{commandId:N}-{sha256}.wav";
            var inputUri = $"gs://{Bucket}/{objectPath}";
            var localObject = Path.Combine(root, objectPath);
            Assert.Equal(source.Length, new FileInfo(localObject).Length);
            await using (var staged = File.OpenRead(localObject))
            {
                Assert.Equal(sha256, Convert.ToHexStringLower(await SHA256.HashDataAsync(staged)));
            }

            source.Position = 0;
            await provider.StageAsync(input, source, media);
            Assert.Single(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));

            speech.Setup(client => client.BatchRecognizeAsync(It.IsAny<BatchRecognizeRequest>(), It.IsAny<CallSettings>()))
                .ReturnsAsync((BatchRecognizeRequest request, CallSettings settings) =>
                {
                    Assert.Equal($"projects/{ProjectNumber}/locations/{Location}/recognizers/_", request.Recognizer);
                    Assert.Equal("long", request.Config.Model);
                    Assert.Equal("uk-UA", Assert.Single(request.Config.LanguageCodes));
                    Assert.NotNull(request.Config.AutoDecodingConfig);
                    Assert.True(request.Config.Features.EnableWordTimeOffsets);
                    Assert.NotNull(request.RecognitionOutputConfig.InlineResponseConfig);
                    Assert.Equal(inputUri, Assert.Single(request.Files).Uri);
                    Assert.Equal(1, settings.Retry!.MaxAttempts);
                    return new Operation<BatchRecognizeResponse, OperationMetadata>(
                        new Operation { Name = Name }, operations.Object);
                });

            var accepted = await provider.StartAsync(input);
            Assert.Equal(Name, accepted.Name);
            Assert.Equal(input, accepted.Input);
            var reopened = CreateProvider(speech.Object, root);

            operations.SetupSequence(client => client.GetOperationAsync(Name, CancellationToken.None))
                .ReturnsAsync(new Operation { Name = Name })
                .ReturnsAsync(Completed(inputUri, includeOffsets: true));
            Assert.Equal(GoogleSpeechV2TranscriptionState.Pending, (await reopened.ReadAsync(accepted)).State);
            var completed = await reopened.ReadAsync(accepted);
            Assert.Equal(GoogleSpeechV2TranscriptionState.Processed, completed.State);
            Assert.NotNull(completed.Transcript);
            Assert.Equal("uk-UA", completed.Transcript.Language);
            var segment = Assert.Single(completed.Transcript.Segments);
            Assert.Equal("Погодьте бюджет із командою.", segment.Text);
            Assert.Equal(TimeSpan.FromSeconds(1.25), segment.Start);
            Assert.Equal(TimeSpan.FromSeconds(3.5), segment.End);

            await reopened.DeleteStagedAsync(input);
            await reopened.DeleteStagedAsync(input);
            Assert.False(File.Exists(localObject));
            speech.Verify(client => client.BatchRecognizeAsync(It.IsAny<BatchRecognizeRequest>(), It.IsAny<CallSettings>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsForeignOperationsAndShowsCancellationOnlyAfterGoogleConfirmsIt()
    {
        var operations = new Mock<OperationsClient>(MockBehavior.Strict);
        var speech = new Mock<SpeechClient>(MockBehavior.Strict);
        speech.SetupGet(client => client.BatchRecognizeOperationsClient).Returns(operations.Object);
        var provider = CreateProvider(speech.Object);
        var input = provider.CreateInputReference(Guid.NewGuid(), SourceHash, new StreamInfo(extension: "webm"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.ReadAsync(new GoogleSpeechV2TranscriptionOperation(Name.Replace("/eu/", "/us/"), input)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.StartAsync(input with { BucketName = "another-company" }));

        var accepted = new GoogleSpeechV2TranscriptionOperation(Name, input);
        operations.Setup(client => client.CancelOperationAsync(Name, CancellationToken.None)).Returns(Task.CompletedTask);
        operations.SetupSequence(client => client.GetOperationAsync(Name, CancellationToken.None))
            .ReturnsAsync(new Operation { Name = Name })
            .ReturnsAsync(new Operation
            {
                Name = Name,
                Done = true,
                Error = new Google.Rpc.Status { Code = 1, Message = "Provider details are private." }
            });

        await provider.CancelAsync(accepted);
        Assert.Equal(GoogleSpeechV2TranscriptionState.Pending, (await provider.ReadAsync(accepted)).State);
        var cancelled = await provider.ReadAsync(accepted);
        Assert.Equal(GoogleSpeechV2TranscriptionState.Cancelled, cancelled.State);
        Assert.Equal("google-rpc-1", cancelled.ErrorCode);
        Assert.Null(cancelled.Transcript);
        operations.Verify(client => client.CancelOperationAsync(Name, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ReportsMissingWordOffsetsWithoutPublishingUntimedText()
    {
        var operations = new Mock<OperationsClient>(MockBehavior.Strict);
        var speech = new Mock<SpeechClient>(MockBehavior.Strict);
        speech.SetupGet(client => client.BatchRecognizeOperationsClient).Returns(operations.Object);
        var provider = CreateProvider(speech.Object);
        var id = Guid.NewGuid();
        var input = provider.CreateInputReference(id, SourceHash, new StreamInfo(extension: "mp4"));
        operations.Setup(client => client.GetOperationAsync(Name, CancellationToken.None))
            .ReturnsAsync(Completed($"gs://{Bucket}/markitdown-transcription/{id:N}-{SourceHash}.mp4", includeOffsets: false));

        var failed = await provider.ReadAsync(new GoogleSpeechV2TranscriptionOperation(Name, input));
        Assert.Equal(GoogleSpeechV2TranscriptionState.Failed, failed.State);
        Assert.Equal("missing-word-offsets", failed.ErrorCode);
        Assert.Null(failed.Transcript);
    }

    [Fact]
    public async Task RecoversOnlyTheExactStagedOperationAndRejectsMultipleMatches()
    {
        var operations = new Mock<OperationsClient>(MockBehavior.Strict);
        var speech = new Mock<SpeechClient>(MockBehavior.Strict);
        speech.SetupGet(client => client.BatchRecognizeOperationsClient).Returns(operations.Object);
        var provider = CreateProvider(speech.Object);
        var commandId = Guid.NewGuid();
        var input = provider.CreateInputReference(commandId, SourceHash, new StreamInfo(extension: "webm"));
        var stagedUri = $"gs://{Bucket}/markitdown-transcription/{commandId:N}-{SourceHash}.webm";
        var wrongRegion = InProgress(Name.Replace("/eu/", "/us/"), stagedUri);
        var wrongObject = InProgress(Name, stagedUri.Replace(".webm", ".mp4"));
        var accepted = InProgress(Name, stagedUri);
        var duplicate = InProgress($"{Name}-duplicate", stagedUri);
        operations.SetupSequence(client => client.ListOperationsAsync(
                It.Is<ListOperationsRequest>(request =>
                    request.Name == $"projects/{ProjectNumber}/locations/{Location}" &&
                    request.Filter == "is_v2(name)"),
                It.IsAny<CallSettings>()))
            .Returns(OperationPages(wrongRegion, wrongObject))
            .Returns(OperationPages(wrongObject, accepted))
            .Returns(OperationPages(accepted, duplicate));

        Assert.Null(await provider.FindOperationAsync(input));
        Assert.Equal(new GoogleSpeechV2TranscriptionOperation(Name, input), await provider.FindOperationAsync(input));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.FindOperationAsync(input));
        speech.Verify(client => client.BatchRecognizeAsync(It.IsAny<BatchRecognizeRequest>(), It.IsAny<CallSettings>()),
            Times.Never);
    }

    private static PagedAsyncEnumerable<ListOperationsResponse, Operation> OperationPages(params Operation[] operations)
    {
        var pages = new Mock<PagedAsyncEnumerable<ListOperationsResponse, Operation>>(MockBehavior.Strict);
        pages.Setup(page => page.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) => operations.ToAsyncEnumerable().GetAsyncEnumerator(token));
        return pages.Object;
    }

    private static Operation InProgress(string name, string inputUri) =>
        new()
        {
            Name = name,
            Metadata = Any.Pack(new OperationMetadata
            {
                BatchRecognizeRequest = new BatchRecognizeRequest
                {
                    Files = { new BatchRecognizeFileMetadata { Uri = inputUri } }
                }
            })
        };

    private static GoogleSpeechV2TranscriptionProvider CreateProvider(SpeechClient client, string? storageRoot = null) =>
        new(new GoogleSpeechV2TranscriptionOptions
        {
            ProjectId = ProjectNumber,
            Location = Location,
            BucketName = Bucket,
            LanguageCode = "uk-UA",
            Model = "long",
            Credential = GoogleCredential.FromAccessToken("unused")
        }, client, () => new FileSystemStorage(new FileSystemStorageOptions
        {
            BaseFolder = storageRoot ?? throw new InvalidOperationException("This scenario does not stage files.")
        }));

    private static Operation Completed(string inputUri, bool includeOffsets)
    {
        var alternative = new SpeechRecognitionAlternative { Transcript = "Погодьте бюджет із командою." };
        if (includeOffsets)
        {
            alternative.Words.Add(new WordInfo
            {
                Word = "Погодьте",
                StartOffset = Duration.FromTimeSpan(TimeSpan.FromSeconds(1.25)),
                EndOffset = Duration.FromTimeSpan(TimeSpan.FromSeconds(2))
            });
            alternative.Words.Add(new WordInfo
            {
                Word = "командою.",
                StartOffset = Duration.FromTimeSpan(TimeSpan.FromSeconds(3)),
                EndOffset = Duration.FromTimeSpan(TimeSpan.FromSeconds(3.5))
            });
        }

        var result = new BatchRecognizeResponse();
        result.Results.Add(inputUri, new BatchRecognizeFileResult
        {
            InlineResult = new InlineResult
            {
                Transcript = new BatchRecognizeResults
                {
                    Results = { new SpeechRecognitionResult { Alternatives = { alternative } } }
                }
            }
        });
        return new Operation { Name = Name, Done = true, Response = Any.Pack(result) };
    }
}
