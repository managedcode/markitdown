using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Rekognition.Model;
using Amazon.S3.Model;
using Amazon.Textract.Model;
using Amazon.TranscribeService.Model;
using MarkItDown;
using MarkItDown.Intelligence.Models;
using MarkItDown.Intelligence.Providers.Aws;
using Moq;
using Shouldly;
using Xunit;

namespace MarkItDown.Tests.Intelligence;

public class AwsProvidersTests
{
    [Fact]
    public async Task DocumentProvider_ExtractsTableCells()
    {
        var blocks = new List<Block>
        {
            new() { Id = "page1", BlockType = "PAGE", Page = 1 },
            new() { Id = "line1", BlockType = "LINE", Page = 1, Text = "Name 42" },
            new()
            {
                Id = "table1",
                BlockType = "TABLE",
                Page = 1,
                Relationships = new List<Relationship>
                {
                    new Relationship { Type = "CHILD", Ids = new List<string> { "cell1", "cell2" } }
                }
            },
            new()
            {
                Id = "cell1",
                BlockType = "CELL",
                Page = 1,
                RowIndex = 1,
                ColumnIndex = 1,
                Relationships = new List<Relationship>
                {
                    new Relationship { Type = "CHILD", Ids = new List<string> { "word1" } }
                }
            },
            new()
            {
                Id = "cell2",
                BlockType = "CELL",
                Page = 1,
                RowIndex = 1,
                ColumnIndex = 2,
                Relationships = new List<Relationship>
                {
                    new Relationship { Type = "CHILD", Ids = new List<string> { "word2" } }
                }
            },
            new() { Id = "word1", BlockType = "WORD", Page = 1, Text = "Name" },
            new() { Id = "word2", BlockType = "WORD", Page = 1, Text = "42" }
        };

        var response = new AnalyzeDocumentResponse
        {
            Blocks = blocks
        };

        var mockTextract = new Mock<Amazon.Textract.IAmazonTextract>();
        mockTextract
            .Setup(c => c.AnalyzeDocumentAsync(It.IsAny<AnalyzeDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var provider = new AwsDocumentIntelligenceProvider(new AwsDocumentIntelligenceOptions
        {
            Region = "us-east-1"
        }, mockTextract.Object);

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "application/pdf", extension: ".pdf", fileName: "sample.pdf");

        var result = await provider.AnalyzeAsync(stream, streamInfo, CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Tables.Count.ShouldBe(1);
        result.Tables[0].Rows[0][0].ShouldBe("Name");
        result.Pages[0].Text.ShouldContain("Name 42");
    }

    [Fact]
    public async Task ImageProvider_ProducesCaption()
    {
        var labelsResponse = new DetectLabelsResponse
        {
            Labels = new List<Label>
            {
                new Label
                {
                    Name = "Document",
                    Confidence = 95f,
                    Instances = new List<Instance> { new Instance() }
                },
                new Label { Name = "Paper", Confidence = 85f }
            }
        };

        var textResponse = new DetectTextResponse
        {
            TextDetections = new List<TextDetection>
            {
                new TextDetection { Type = "LINE", DetectedText = "Invoice" },
                new TextDetection { Type = "LINE", DetectedText = "Total" }
            }
        };

        var mockRekognition = new Mock<Amazon.Rekognition.IAmazonRekognition>();
        mockRekognition
            .Setup(r => r.DetectLabelsAsync(It.IsAny<DetectLabelsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(labelsResponse);
        mockRekognition
            .Setup(r => r.DetectTextAsync(It.IsAny<DetectTextRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(textResponse);

        var provider = new AwsImageUnderstandingProvider(new AwsVisionOptions
        {
            Region = "us-east-1",
            MinConfidence = 70f
        }, mockRekognition.Object);

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "image/png", extension: ".png", fileName: "image.png");

        var result = await provider.AnalyzeAsync(stream, streamInfo, CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Caption.ShouldBe("Document");
        result.Text.ShouldNotBeNull();
        result.Text!.ShouldContain("Invoice");
        result.DetectedObjects.ShouldContain("Document");
    }

    [Fact]
    public async Task MediaProvider_ParsesTranscriptJson()
    {
        var transcriptJson = "{" +
            "\"results\":{" +
            "\"transcripts\":[{\"transcript\":\"Hello world.\"}]," +
            "\"items\":[" +
            "{\"type\":\"pronunciation\",\"start_time\":\"0.0\",\"end_time\":\"1.0\",\"alternatives\":[{\"content\":\"Hello\"}]}," +
            "{\"type\":\"pronunciation\",\"start_time\":\"1.0\",\"end_time\":\"2.0\",\"alternatives\":[{\"content\":\"world\"}]}," +
            "{\"type\":\"punctuation\",\"alternatives\":[{\"content\":\".\"}]}" +
            "]}}";

        var handler = new StubHttpMessageHandler(transcriptJson);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com/")
        };

        var mockS3 = new Mock<Amazon.S3.IAmazonS3>();
        mockS3
            .Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse());
        mockS3
            .Setup(s => s.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        var statusQueue = new Queue<string>(new[] { "IN_PROGRESS", "COMPLETED" });

        var mockTranscribe = new Mock<Amazon.TranscribeService.IAmazonTranscribeService>();
        mockTranscribe
            .Setup(t => t.StartTranscriptionJobAsync(It.IsAny<StartTranscriptionJobRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartTranscriptionJobResponse
            {
                TranscriptionJob = new TranscriptionJob
                {
                    TranscriptionJobStatus = "IN_PROGRESS"
                }
            });

        mockTranscribe
            .Setup(t => t.GetTranscriptionJobAsync(It.IsAny<GetTranscriptionJobRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var status = statusQueue.Count > 0 ? statusQueue.Dequeue() : "COMPLETED";
                return new GetTranscriptionJobResponse
                {
                    TranscriptionJob = new TranscriptionJob
                    {
                        TranscriptionJobStatus = status,
                        Transcript = string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
                            ? new Transcript { TranscriptFileUri = "https://example.com/transcript.json" }
                            : null
                    }
                };
            });

        var provider = new AwsMediaTranscriptionProvider(new AwsMediaIntelligenceOptions
        {
            Region = "us-east-1",
            InputBucketName = "markitdown-input",
            OutputBucketName = "markitdown-output",
            DeleteInputOnCompletion = true,
            PollingInterval = TimeSpan.FromMilliseconds(10)
        }, mockTranscribe.Object, mockS3.Object, httpClient);

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "audio/wav", extension: ".wav", fileName: "audio.wav");

        var result = await provider.TranscribeAsync(stream, streamInfo, CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Segments.Count.ShouldBe(1);
        result.Segments[0].Text.ShouldBe("Hello world.");
        result.Segments[0].Start.ShouldBe(TimeSpan.Zero);
        result.Segments[0].End.ShouldBe(TimeSpan.FromSeconds(2));

        mockS3.Verify(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        mockS3.Verify(s => s.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string content;

        public StubHttpMessageHandler(string content)
        {
            this.content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            };

            return Task.FromResult(response);
        }
    }
}
