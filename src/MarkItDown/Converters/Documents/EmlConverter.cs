using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;
using MarkItDown;
using MimeKit;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for EML (email) files that extracts headers, content, and attachment metadata.
/// </summary>
public sealed class EmlConverter : DocumentPipelineConverterBase
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".eml"
    };

    private static readonly string[] AcceptedMimeTypePrefixes =
    {
        "message/rfc822",
        "message/email",
        "application/email",
        "text/email"
    };

    private readonly HtmlConverter htmlConverter;
    private readonly SegmentOptions segmentOptions;
    private readonly IConversionPipeline conversionPipeline;

    public EmlConverter(SegmentOptions? segmentOptions = null, IConversionPipeline? pipeline = null, HtmlConverter? htmlConverter = null)
        : base(priority: 240)
    {
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
        conversionPipeline = pipeline ?? ConversionPipeline.Empty;
        this.htmlConverter = htmlConverter ?? new HtmlConverter();
    }

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = streamInfo.ResolveMimeType();
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && AcceptedExtensions.Contains(extension))
        {
            return true;
        }

        return StreamInfo.MatchesMime(normalizedMime, AcceptedMimeTypePrefixes) ||
               StreamInfo.MatchesMime(streamInfo.MimeType, AcceptedMimeTypePrefixes);
    }

    private SegmentOptions ResolveSegmentOptions()
        => ConversionContextAccessor.Current?.Segments ?? segmentOptions;

    private ArtifactStorageOptions ResolveStorageOptions()
        => ConversionContextAccessor.Current?.Storage ?? ArtifactStorageOptions.Default;

    public override async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        ArtifactWorkspace? workspace = null;
        var storageOptions = ResolveStorageOptions();
        var effectiveSegments = ResolveSegmentOptions();
        string? storedSourcePath = null;
        string? storedMarkdownPath = null;

        try
        {
            await using var source = await MaterializeSourceAsync(stream, streamInfo, ".eml", cancellationToken).ConfigureAwait(false);

            var defaultMime = MimeHelper.GetMimeType(".eml") ?? "message/rfc822";
            workspace = WorkspacePersistence.CreateWorkspace(streamInfo, effectiveSegments, storageOptions, source.FilePath, ".eml", defaultMime, cancellationToken, out storedSourcePath);

            await using var fileStream = OpenReadOnlyFile(source.FilePath);
            var message = await MimeMessage.LoadAsync(fileStream, cancellationToken).ConfigureAwait(false);

            var conversion = await ConvertEmailToMarkdownAsync(message, cancellationToken).ConfigureAwait(false);
            var extraction = BuildExtraction(conversion.Markdown, streamInfo, conversion.Attachments);

            await conversionPipeline.ExecuteAsync(streamInfo, extraction.Artifacts, extraction.Segments, cancellationToken).ConfigureAwait(false);

            var generatedAt = DateTime.UtcNow;
            var titleHint = ExtractTitle(message);
            var meta = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, streamInfo, effectiveSegments, titleHint, generatedAt);
            var fallbackTitle = Path.GetFileNameWithoutExtension(streamInfo.FileName ?? streamInfo.LocalPath ?? streamInfo.Url) ?? "Email Message";
            var title = meta.Title ?? titleHint ?? fallbackTitle;

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataKeys.DocumentTitle] = title,
                [MetadataKeys.DocumentPages] = extraction.Segments.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentImages] = extraction.Artifacts.Images.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentTables] = extraction.Artifacts.Tables.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.EmailAttachmentsCount] = conversion.Attachments.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.WorkspaceDirectory] = workspace.DirectoryPath
            };

            if (!string.IsNullOrWhiteSpace(titleHint))
            {
                metadata[MetadataKeys.DocumentTitleHint] = titleHint!;
            }

            if (conversion.Attachments.Count > 0)
            {
                metadata[MetadataKeys.EmailAttachments] = string.Join("; ", conversion.Attachments.Select(static attachment => attachment.Name));
            }

            foreach (var pair in extraction.Artifacts.Metadata)
            {
                metadata[pair.Key] = pair.Value;
            }

            if (!string.IsNullOrWhiteSpace(storedSourcePath))
            {
                metadata[MetadataKeys.WorkspaceSourceFile] = storedSourcePath;
            }

            storedMarkdownPath = WorkspacePersistence.PersistMarkdown(workspace, storageOptions, streamInfo, meta.Markdown, cancellationToken);
            if (!string.IsNullOrWhiteSpace(storedMarkdownPath))
            {
                metadata[MetadataKeys.WorkspaceMarkdownFile] = storedMarkdownPath;
            }

            string MarkdownFactory()
            {
                var recomposed = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, streamInfo, effectiveSegments, titleHint, generatedAt);
                return recomposed.Markdown;
            }

            return DocumentConverterResult.FromFactory(
                MarkdownFactory,
                title,
                extraction.Segments,
                extraction.Artifacts,
                metadata,
                artifactDirectory: workspace.DirectoryPath,
                cleanup: null,
                asyncCleanup: workspace,
                generatedAtUtc: generatedAt);
        }
        catch (Exception ex) when (ex is not MarkItDownException)
        {
            if (workspace is not null)
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
            }

            throw new FileConversionException($"Failed to convert EML file: {ex.Message}", ex);
        }
        catch
        {
            if (workspace is not null)
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private async Task<EmailConversionResult> ConvertEmailToMarkdownAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();

        builder.AppendLine("# Email");
        builder.AppendLine();

        if (!string.IsNullOrEmpty(message.Subject))
        {
            builder.AppendLine($"**Subject:** {EscapeMarkdown(message.Subject)}");
        }

        if (message.From?.Count > 0)
        {
            builder.AppendLine($"**From:** {EscapeMarkdown(string.Join(", ", message.From.Select(FormatAddress)))}");
        }

        if (message.To?.Count > 0)
        {
            builder.AppendLine($"**To:** {EscapeMarkdown(string.Join(", ", message.To.Select(FormatAddress)))}");
        }

        if (message.Cc?.Count > 0)
        {
            builder.AppendLine($"**CC:** {EscapeMarkdown(string.Join(", ", message.Cc.Select(FormatAddress)))}");
        }

        if (message.Date != DateTimeOffset.MinValue)
        {
            builder.AppendLine($"**Date:** {message.Date:yyyy-MM-dd HH:mm:ss zzz}");
        }

        if (!string.IsNullOrEmpty(message.MessageId))
        {
            builder.AppendLine($"**Message-ID:** {EscapeMarkdown(message.MessageId)}");
        }

        builder.AppendLine();

        var bodyContent = await ExtractBodyContentAsync(message, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(bodyContent))
        {
            builder.AppendLine("## Message Content");
            builder.AppendLine();
            builder.AppendLine(bodyContent.TrimEnd());
            builder.AppendLine();
        }

        var attachments = ExtractAttachmentInfo(message);
        if (attachments.Count > 0)
        {
            builder.AppendLine("## Attachments");
            builder.AppendLine();
            foreach (var attachment in attachments)
            {
                builder.AppendLine($"- **{EscapeMarkdown(attachment.Name)}** ({attachment.ContentType}) - {attachment.Size}");
            }
            builder.AppendLine();
        }

        return new EmailConversionResult(builder.ToString().TrimEnd(), attachments);
    }

    private async Task<string> ExtractBodyContentAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        if (message.Body is null)
        {
            return string.Empty;
        }

        var htmlBody = message.HtmlBody;
        if (!string.IsNullOrWhiteSpace(htmlBody))
        {
            try
            {
                var payload = Encoding.UTF8.GetBytes(htmlBody);
                await using var htmlHandle = await DiskBufferHandle.FromBytesAsync(payload, ".html", cancellationToken).ConfigureAwait(false);
                await using var htmlStream = htmlHandle.OpenRead();
                var htmlStreamInfo = new StreamInfo(mimeType: "text/html", charset: Encoding.UTF8);
                await using var result = await htmlConverter.ConvertAsync(htmlStream, htmlStreamInfo, cancellationToken).ConfigureAwait(false);
                return result.Markdown;
            }
            catch
            {
                return EscapeMarkdown(htmlBody);
            }
        }

        var textBody = message.TextBody;
        return !string.IsNullOrEmpty(textBody) ? EscapeMarkdown(textBody) : string.Empty;
    }

    private static List<AttachmentInfo> ExtractAttachmentInfo(MimeMessage message)
    {
        var attachments = new List<AttachmentInfo>();

        foreach (var attachment in message.Attachments)
        {
            var name = attachment.ContentDisposition?.FileName ??
                       attachment.ContentType?.Name ??
                       "Unknown";

            var contentType = attachment.ContentType?.ToString() ?? "application/octet-stream";

            var size = "Unknown size";
            if (attachment is MimePart part)
            {
                try
                {
                    if (part.ContentDisposition?.Size.HasValue == true)
                    {
                        size = FileUtilities.FormatFileSize(part.ContentDisposition.Size.Value);
                    }
                    else if (part.Headers.Contains("Content-Length") &&
                             long.TryParse(part.Headers["Content-Length"], out var contentLength))
                    {
                        size = FileUtilities.FormatFileSize(contentLength);
                    }
                }
                catch
                {
                    // ignore parsing issues
                }
            }

            attachments.Add(new AttachmentInfo(name, contentType, size));
        }

        return attachments;
    }

    private static string FormatAddress(InternetAddress address)
        => address switch
        {
            MailboxAddress mailbox when !string.IsNullOrEmpty(mailbox.Name) => $"{mailbox.Name} <{mailbox.Address}>",
            MailboxAddress mailbox => mailbox.Address,
            _ => address.ToString()
        };

    private static string ExtractTitle(MimeMessage message)
    {
        if (!string.IsNullOrEmpty(message.Subject))
        {
            return message.Subject.Trim();
        }

        var sender = message.From?.FirstOrDefault();
        return sender is not null
            ? $"Email from {FormatAddress(sender)}"
            : "Email Message";
    }

    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    private EmlExtractionResult BuildExtraction(string markdown, StreamInfo streamInfo, IReadOnlyList<AttachmentInfo> attachments)
    {
        var normalized = TextSanitizer.Normalize(markdown, trim: true);
        var segments = new List<DocumentSegment>();
        var artifacts = new ConversionArtifacts();

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Page] = "1"
            };

            var segment = new DocumentSegment(
                markdown: normalized,
                type: SegmentType.Page,
                number: 1,
                label: "Page 1",
                source: streamInfo.FileName,
                additionalMetadata: metadata);

            segments.Add(segment);
            artifacts.TextBlocks.Add(new TextArtifact(normalized, 1, streamInfo.FileName, segment.Label));
        }

        if (attachments.Count > 0)
        {
            artifacts.Metadata[MetadataKeys.EmailAttachmentsCount] = attachments.Count.ToString(CultureInfo.InvariantCulture);
            artifacts.Metadata[MetadataKeys.EmailAttachments] = string.Join("; ", attachments.Select(static attachment => attachment.Name));
        }

        return new EmlExtractionResult(segments, artifacts);
    }

    private sealed record AttachmentInfo(string Name, string ContentType, string Size);

    private sealed record EmailConversionResult(string Markdown, IReadOnlyList<AttachmentInfo> Attachments);

    private sealed record EmlExtractionResult(List<DocumentSegment> Segments, ConversionArtifacts Artifacts);
}
