using System;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using WpDrawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;

namespace MarkItDown.Tests.Fixtures;

internal static class DocxInlineImageFactory
{
    private const string PixelBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGJ8/P8/AwAI/AL+Kc3sNwAAAABJRU5ErkJggg==";

    public static MemoryStream Create(string beforeText = "Paragraph before image.", string afterText = "Paragraph after image.")
    {
        var workingStream = new MemoryStream();

        using (var document = WordprocessingDocument.Create(workingStream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body ?? throw new InvalidOperationException("DOCX body was not created.");

            body.AppendChild(new Paragraph(new Run(new Text(beforeText))));

            AppendInlineImage(mainPart, body);

            body.AppendChild(new Paragraph(new Run(new Text(afterText))));

            mainPart.Document.Save();
        }

        var buffer = workingStream.ToArray();
        return new MemoryStream(buffer, writable: false);
    }

    private static void AppendInlineImage(MainDocumentPart mainPart, Body body)
    {
        var imagePart = mainPart.AddImagePart(ImagePartType.Png);
        using (var imageStream = new MemoryStream(Convert.FromBase64String(PixelBase64)))
        {
            imagePart.FeedData(imageStream);
        }

        var relationshipId = mainPart.GetIdOfPart(imagePart);
        const long size = 990000L;

        var inline = new DW.Inline(
            new DW.Extent { Cx = size, Cy = size },
            new DW.EffectExtent
            {
                LeftEdge = 0L,
                TopEdge = 0L,
                RightEdge = 0L,
                BottomEdge = 0L,
            },
            new DW.DocProperties
            {
                Id = 1U,
                Name = "Test image",
                Description = "Generated pixel",
            },
            new DW.NonVisualGraphicFrameDrawingProperties(
                new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(
                new A.GraphicData(
                    new PIC.Picture(
                        new PIC.NonVisualPictureProperties(
                            new PIC.NonVisualDrawingProperties
                            {
                                Id = 0U,
                                Name = "pixel.png",
                                Description = "Generated pixel",
                            },
                            new PIC.NonVisualPictureDrawingProperties()),
                        new PIC.BlipFill(
                            new A.Blip
                            {
                                Embed = relationshipId,
                                CompressionState = A.BlipCompressionValues.Print,
                            },
                            new A.Stretch(new A.FillRectangle())),
                        new PIC.ShapeProperties(
                            new A.Transform2D(
                                new A.Offset { X = 0L, Y = 0L },
                                new A.Extents { Cx = size, Cy = size }),
                            new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle }))
                )
                {
                    Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture",
                })
        )
        {
            DistanceFromTop = 0U,
            DistanceFromBottom = 0U,
            DistanceFromLeft = 0U,
            DistanceFromRight = 0U,
        };

        var run = new Run(new WpDrawing(inline));
        body.AppendChild(new Paragraph(run));
    }
}
