using System;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using V = DocumentFormat.OpenXml.Vml;

namespace MarkItDown.Tests.Fixtures;

internal static class DocxLegacyImageFactory
{
    private const string PixelBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGJ8/P8/AwAI/AL+Kc3sNwAAAABJRU5ErkJggg==";

    public static MemoryStream Create(string beforeText = "Legacy paragraph before image.", string afterText = "Legacy paragraph after image.")
    {
        var workingStream = new MemoryStream();

        using (var document = WordprocessingDocument.Create(workingStream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body ?? throw new InvalidOperationException("DOCX body was not created.");

            body.AppendChild(new Paragraph(new Run(new Text(beforeText))));

            AppendLegacyImage(mainPart, body);

            body.AppendChild(new Paragraph(new Run(new Text(afterText))));

            mainPart.Document.Save();
        }

        var buffer = workingStream.ToArray();
        return new MemoryStream(buffer, writable: false);
    }

    private static void AppendLegacyImage(MainDocumentPart mainPart, Body body)
    {
        var imagePart = mainPart.AddImagePart(ImagePartType.Png);
        using (var imageStream = new MemoryStream(Convert.FromBase64String(PixelBase64)))
        {
            imagePart.FeedData(imageStream);
        }

        var relationshipId = mainPart.GetIdOfPart(imagePart);

        var shapeType = new V.Shapetype
        {
            Id = "_x0000_t75",
            CoordinateSize = "21600,21600",
            OptionalNumber = 75,
            PreferRelative = true,
            Filled = false,
            Stroked = false
        };

        shapeType.AppendChild(new V.Stroke { JoinStyle = V.StrokeJoinStyleValues.Miter });

        var shape = new V.Shape
        {
            Id = "legacyImage1",
            Type = "#_x0000_t75",
            Style = "width:16pt;height:16pt"
        };

        shape.AppendChild(new V.ImageData { RelationshipId = relationshipId, Title = "Legacy image" });

        var picture = new Picture(shapeType, shape);
        var run = new Run(new RunProperties(new RunStyle { Val = "Normal" }), picture);
        body.AppendChild(new Paragraph(run));
    }
}
