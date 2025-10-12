using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Api.Gax.Grpc;
using Google.Cloud.DocumentAI.V1;
using Google.Cloud.Speech.V1;
using Google.Cloud.Vision.V1;
using Google.Protobuf.WellKnownTypes;
using MarkItDown;
using MarkItDown.Intelligence.Models;
using MarkItDown.Intelligence.Providers.Google;
using Moq;
using Shouldly;
using Xunit;

namespace MarkItDown.Tests.Intelligence;

public class GoogleProvidersTests
{
    [Fact]
    public async Task DocumentProvider_ConvertsPagesAndTables()
    {
        var documentText = "Header Value Row 42";
        var document = new Document
        {
            Text = documentText
        };

        var pageAnchor = new Document.Types.TextAnchor();
        pageAnchor.TextSegments.Add(new Document.Types.TextAnchor.Types.TextSegment
        {
            StartIndex = 0,
            EndIndex = documentText.Length
        });

        var headerAnchor = new Document.Types.TextAnchor();
        headerAnchor.TextSegments.Add(new Document.Types.TextAnchor.Types.TextSegment
        {
            StartIndex = 0,
            EndIndex = 6
        });

        var valueAnchor = new Document.Types.TextAnchor();
        valueAnchor.TextSegments.Add(new Document.Types.TextAnchor.Types.TextSegment
        {
            StartIndex = 7,
            EndIndex = 12
        });

        var bodyHeaderAnchor = new Document.Types.TextAnchor();
        bodyHeaderAnchor.TextSegments.Add(new Document.Types.TextAnchor.Types.TextSegment
        {
            StartIndex = 13,
            EndIndex = 16
        });

        var bodyValueAnchor = new Document.Types.TextAnchor();
        bodyValueAnchor.TextSegments.Add(new Document.Types.TextAnchor.Types.TextSegment
        {
            StartIndex = 17,
            EndIndex = documentText.Length
        });

        var table = new Document.Types.Page.Types.Table();
        table.HeaderRows.Add(new Document.Types.Page.Types.Table.Types.TableRow
        {
            Cells =
            {
                new Document.Types.Page.Types.Table.Types.TableCell
                {
                    Layout = new Document.Types.Page.Types.Layout { TextAnchor = headerAnchor }
                },
                new Document.Types.Page.Types.Table.Types.TableCell
                {
                    Layout = new Document.Types.Page.Types.Layout { TextAnchor = valueAnchor }
                }
            }
        });

        table.BodyRows.Add(new Document.Types.Page.Types.Table.Types.TableRow
        {
            Cells =
            {
                new Document.Types.Page.Types.Table.Types.TableCell
                {
                    Layout = new Document.Types.Page.Types.Layout { TextAnchor = bodyHeaderAnchor }
                },
                new Document.Types.Page.Types.Table.Types.TableCell
                {
                    Layout = new Document.Types.Page.Types.Layout { TextAnchor = bodyValueAnchor }
                }
            }
        });

        var page = new Document.Types.Page
        {
            PageNumber = 1,
            Layout = new Document.Types.Page.Types.Layout { TextAnchor = pageAnchor },
            Dimension = new Document.Types.Page.Types.Dimension
            {
                Width = 8.5f,
                Height = 11f,
                Unit = "inch"
            },
            Tables = { table }
        };

        document.Pages.Add(page);

        var mockClient = new Mock<DocumentProcessorServiceClient>();
        mockClient
            .Setup(c => c.ProcessDocumentAsync(It.IsAny<ProcessRequest>(), It.IsAny<CallSettings>()))
            .ReturnsAsync(new ProcessResponse { Document = document });

        var provider = new GoogleDocumentIntelligenceProvider(new GoogleDocumentIntelligenceOptions
        {
            ProjectId = "proj",
            Location = "us",
            ProcessorId = "processor"
        }, mockClient.Object);

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "application/pdf", extension: ".pdf", fileName: "sample.pdf");

        var result = await provider.AnalyzeAsync(stream, streamInfo, request: null, CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Pages.Count.ShouldBe(1);
        result.Pages[0].Text.ShouldContain("Header");
        result.Tables.Count.ShouldBe(1);
        result.Tables[0].Rows.Count.ShouldBe(2);
        result.Tables[0].Rows[1][1].ShouldBe("42");
    }

    [Fact]
    public async Task ImageProvider_ProducesCaptionAndText()
    {
        var annotateResponse = new BatchAnnotateImagesResponse();
        annotateResponse.Responses.Add(new AnnotateImageResponse
        {
            LabelAnnotations =
            {
                new EntityAnnotation { Description = "Laptop", Score = 0.95f },
                new EntityAnnotation { Description = "Desk", Score = 0.80f }
            },
            FullTextAnnotation = new TextAnnotation { Text = "Invoice" },
            LocalizedObjectAnnotations =
            {
                new LocalizedObjectAnnotation { Name = "Computer", Score = 0.88f }
            },
            SafeSearchAnnotation = new SafeSearchAnnotation
            {
                Adult = Likelihood.Unlikely,
                Medical = Likelihood.VeryUnlikely,
                Racy = Likelihood.Unlikely,
                Violence = Likelihood.VeryUnlikely
            }
        });

        var mockClient = new Mock<ImageAnnotatorClient>();
        mockClient
            .Setup(c => c.BatchAnnotateImagesAsync(It.IsAny<BatchAnnotateImagesRequest>(), It.IsAny<CallSettings>()))
            .ReturnsAsync(annotateResponse);

        var provider = new GoogleImageUnderstandingProvider(new GoogleVisionOptions
        {
            MaxLabels = 5
        }, mockClient.Object);

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "image/png", extension: ".png", fileName: "image.png");

        var result = await provider.AnalyzeAsync(stream, streamInfo, request: null, CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Caption.ShouldBe("Laptop");
        result.Text.ShouldBe("Invoice");
        result.DetectedObjects.ShouldContain("Computer");
        result.Metadata["safeSearch.adult"].ShouldBe(Likelihood.Unlikely.ToString());
    }

    [Fact]
    public async Task MediaProvider_ReturnsTimedSegments()
    {
        var recognizeResponse = new RecognizeResponse
        {
            Results =
            {
                new SpeechRecognitionResult
                {
                    Alternatives =
                    {
                        new SpeechRecognitionAlternative
                        {
                            Transcript = "Hello world.",
                            Confidence = 0.9f,
                            Words =
                            {
                                new WordInfo
                                {
                                    StartTime = Duration.FromTimeSpan(TimeSpan.Zero),
                                    EndTime = Duration.FromTimeSpan(TimeSpan.FromSeconds(1))
                                },
                                new WordInfo
                                {
                                    StartTime = Duration.FromTimeSpan(TimeSpan.FromSeconds(1)),
                                    EndTime = Duration.FromTimeSpan(TimeSpan.FromSeconds(2))
                                }
                            }
                        }
                    }
                }
            }
        };

        var mockClient = new Mock<SpeechClient>();
        mockClient
            .Setup(c => c.RecognizeAsync(It.IsAny<RecognitionConfig>(), It.IsAny<RecognitionAudio>(), It.IsAny<CallSettings>()))
            .ReturnsAsync(recognizeResponse);

        var provider = new GoogleMediaTranscriptionProvider(new GoogleMediaIntelligenceOptions
        {
            LanguageCode = "en-US"
        }, mockClient.Object);

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var streamInfo = new StreamInfo(mimeType: "audio/wav", extension: ".wav", fileName: "audio.wav");

        var result = await provider.TranscribeAsync(stream, streamInfo, request: null, CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Segments.Count.ShouldBe(1);
        result.Segments[0].Text.ShouldBe("Hello world.");
        result.Segments[0].Start.ShouldBe(TimeSpan.Zero);
        result.Segments[0].End.ShouldBe(TimeSpan.FromSeconds(2));
    }
}
