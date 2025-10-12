using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DocumentFormat.OpenXml.Wordprocessing;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using Vml = DocumentFormat.OpenXml.Vml;

namespace MarkItDown.Converters;

public sealed partial class DocxConverter
{
    private static string EnsureImagePlaceholder(ImageArtifact image)
    {
        if (!string.IsNullOrWhiteSpace(image.PlaceholderMarkdown))
        {
            return image.PlaceholderMarkdown!;
        }

        var placeholder = BuildImagePlaceholder(image);
        image.PlaceholderMarkdown = placeholder;
        return placeholder;
    }

    private static int? GetImagePageNumber(ImageArtifact image)
    {
        if (image.PageNumber.HasValue)
        {
            return image.PageNumber.Value;
        }

        if (image.Metadata.TryGetValue(MetadataKeys.Page, out var pageValue) &&
            int.TryParse(pageValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string BuildImagePlaceholder(ImageArtifact artifact)
    {
        var pageNumber = ResolvePageNumber(artifact);
        var prefix = pageNumber.HasValue ? $"Image (page {pageNumber.Value})" : "Image";

        var detailed = NormalizeOrNull(GetDetailedDescription(artifact));
        if (!string.IsNullOrEmpty(detailed))
        {
            var label = NormalizeOrNull(artifact.Label);
            if (!string.IsNullOrEmpty(label) && !string.Equals(label, detailed, StringComparison.OrdinalIgnoreCase))
            {
                return ImagePlaceholderFormatter.BuildPlaceholder(artifact, label, prefix);
            }

            var caption = NormalizeOrNull(GetCaption(artifact));
            if (!string.IsNullOrEmpty(caption) && !string.Equals(caption, detailed, StringComparison.OrdinalIgnoreCase))
            {
                return ImagePlaceholderFormatter.BuildPlaceholder(artifact, caption, prefix);
            }

            return ImagePlaceholderFormatter.BuildPlaceholder(artifact, null, prefix);
        }

        var summary = NormalizeOrNull(artifact.Label)
            ?? NormalizeOrNull(GetCaption(artifact))
            ?? NormalizeOrNull(artifact.RawText);

        if (string.IsNullOrEmpty(summary))
        {
            return ImagePlaceholderFormatter.BuildPlaceholder(artifact, null, prefix);
        }

        artifact.DetailedDescription = summary;
        artifact.Metadata[MetadataKeys.DetailedDescription] = summary;
        artifact.Metadata[MetadataKeys.Caption] = summary;

        return ImagePlaceholderFormatter.BuildPlaceholder(artifact, summary, prefix);
    }

    private static string? GetDetailedDescription(ImageArtifact artifact)
    {
        if (artifact.Metadata.TryGetValue(MetadataKeys.DetailedDescription, out var detailed) && !string.IsNullOrWhiteSpace(detailed))
        {
            return detailed;
        }

        return artifact.DetailedDescription;
    }

    private static string? GetCaption(ImageArtifact artifact)
        => artifact.Metadata.TryGetValue(MetadataKeys.Caption, out var caption) && !string.IsNullOrWhiteSpace(caption)
            ? caption
            : null;

    private static int? ResolvePageNumber(ImageArtifact artifact)
    {
        if (artifact.PageNumber.HasValue)
        {
            return artifact.PageNumber.Value;
        }

        if (artifact.Metadata.TryGetValue(MetadataKeys.Page, out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? NormalizeOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = TextSanitizer.Normalize(value, trim: true);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void AppendImageContent(StringBuilder builder, ImageArtifact artifact)
    {
        var placeholder = EnsureImagePlaceholder(artifact);

        if (builder.Length > 0 && builder[^1] != '\n')
        {
            builder.AppendLine();
        }

        builder.AppendLine(placeholder);
        builder.AppendLine();

        var comment = MetaMarkdownFormatter.BuildImageComment(artifact);
        if (!string.IsNullOrWhiteSpace(comment))
        {
            builder.AppendLine(comment);
            builder.AppendLine();
        }
    }

    private ImageArtifact? TryCreateImageArtifact(
        DocumentFormat.OpenXml.Wordprocessing.Drawing drawing,
        IReadOnlyDictionary<string, DocxImagePart> imageCatalog,
        int pageNumber,
        StreamInfo streamInfo,
        ImageArtifactPersistor? persistor,
        CancellationToken cancellationToken)
    {
        var blip = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
        var relationshipId = blip?.Embed?.Value;
        return TryCreateImageArtifact(relationshipId, imageCatalog, pageNumber, streamInfo, persistor, cancellationToken);
    }

    private ImageArtifact? TryCreateImageArtifact(
        Picture picture,
        IReadOnlyDictionary<string, DocxImagePart> imageCatalog,
        int pageNumber,
        StreamInfo streamInfo,
        ImageArtifactPersistor? persistor,
        CancellationToken cancellationToken)
    {
        var imageData = picture.Descendants<Vml.ImageData>().FirstOrDefault();
        var relationshipId = imageData?.RelationshipId?.Value;
        return TryCreateImageArtifact(relationshipId, imageCatalog, pageNumber, streamInfo, persistor, cancellationToken);
    }

    private ImageArtifact? TryCreateImageArtifact(
        string? relationshipId,
        IReadOnlyDictionary<string, DocxImagePart> imageCatalog,
        int pageNumber,
        StreamInfo streamInfo,
        ImageArtifactPersistor? persistor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            return null;
        }

        if (!imageCatalog.TryGetValue(relationshipId, out var part))
        {
            return null;
        }

        var artifact = new ImageArtifact(part.Data, part.ContentType, pageNumber, streamInfo.FileName);
        persistor?.Persist(artifact, cancellationToken);
        return artifact;
    }

}
