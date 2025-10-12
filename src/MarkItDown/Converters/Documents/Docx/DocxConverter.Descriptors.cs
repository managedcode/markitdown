using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace MarkItDown.Converters;

public sealed partial class DocxConverter
{
    private static List<DocxElementDescriptor> BuildDescriptors(Body body, CancellationToken cancellationToken)
    {
        var descriptors = new List<DocxElementDescriptor>();
        var pageNumber = 1;
        var index = 0;

        foreach (var element in body.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (element)
            {
                case Paragraph paragraph:
                {
                    var breakCount = CountPageBreaks(paragraph, cloneConsumesBreak: true);
                    descriptors.Add(new ParagraphDescriptor(index++, pageNumber, (Paragraph)paragraph.CloneNode(true)));
                    if (breakCount > 0)
                    {
                        pageNumber += breakCount;
                    }

                    break;
                }

                case Table table:
                {
                    var breakCount = CountPageBreaks(table, cloneConsumesBreak: true);
                    descriptors.Add(new TableDescriptor(index++, pageNumber, (Table)table.CloneNode(true)));
                    if (breakCount > 0)
                    {
                        pageNumber += breakCount;
                    }

                    break;
                }

                default:
                {
                    var clone = (OpenXmlElement)element.CloneNode(true);
                    descriptors.Add(new OtherDescriptor(index++, pageNumber, clone));
                    pageNumber += CountPageBreaks(element, cloneConsumesBreak: false);

                    break;
                }
            }
        }

        return descriptors;
    }

    private static IReadOnlyDictionary<string, DocxImagePart> BuildImageCatalog(WordprocessingDocument document, CancellationToken cancellationToken)
    {
        var catalog = new Dictionary<string, DocxImagePart>(StringComparer.Ordinal);
        if (document.MainDocumentPart is null)
        {
            return catalog;
        }

        var stack = new Stack<OpenXmlPartContainer>();
        stack.Push(document.MainDocumentPart);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var container = stack.Pop();

            foreach (var part in container.Parts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (part.OpenXmlPart is ImagePart imagePart)
                {
                    var relationshipId = part.RelationshipId;
                    if (string.IsNullOrEmpty(relationshipId) || catalog.ContainsKey(relationshipId))
                    {
                        continue;
                    }

                    using var partStream = imagePart.GetStream(FileMode.Open, FileAccess.Read);
                    var handle = DiskBufferHandle.FromStream(partStream, Path.GetExtension(imagePart.Uri?.ToString()), bufferSize: 64 * 1024);
                    try
                    {
                        var data = File.ReadAllBytes(handle.FilePath);
                        catalog[relationshipId] = new DocxImagePart(data, imagePart.ContentType);
                    }
                    finally
                    {
                        handle.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    }
                    continue;
                }

                if (part.OpenXmlPart is OpenXmlPart child)
                {
                    stack.Push(child);
                }
            }
        }

        return catalog;
    }

    private static int CountPageBreaks(OpenXmlElement element, bool cloneConsumesBreak)
    {
        var renderedBreaks = element.Descendants<LastRenderedPageBreak>().Count();
        var explicitBreaks = element
            .Descendants<Break>()
            .Count(b => b.Type?.Value == BreakValues.Page);

        var totalBreaks = renderedBreaks + explicitBreaks;
        if (totalBreaks == 0)
        {
            return 0;
        }

        return cloneConsumesBreak ? totalBreaks : 0;
    }


}
