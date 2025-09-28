using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;
using MimeKit;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for EML (email) files that extracts headers, content, and attachment metadata.
/// </summary>
public sealed class EmlConverter : IDocumentConverter
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

    private readonly HtmlConverter _htmlConverter;

    public int Priority => 240; // Between EPUB and PPTX

    public EmlConverter()
    {
        _htmlConverter = new HtmlConverter();
    }

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = MimeTypeUtilities.NormalizeMime(streamInfo);
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && AcceptedExtensions.Contains(extension))
            return true;

        return MimeTypeUtilities.MatchesAny(normalizedMime, AcceptedMimeTypePrefixes)
            || MimeTypeUtilities.MatchesAny(streamInfo.MimeType, AcceptedMimeTypePrefixes);
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (!AcceptsInput(streamInfo))
            return false;

        // For EML files, we rely on extension and MIME type detection
        // as parsing the entire message for detection would be expensive
        return true;
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            if (stream.CanSeek)
                stream.Position = 0;

            var message = await MimeMessage.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
            
            var markdown = await ConvertEmailToMarkdownAsync(message, cancellationToken).ConfigureAwait(false);
            var title = ExtractTitle(message);
            
            return new DocumentConverterResult(markdown, title);
        }
        catch (Exception ex) when (ex is not MarkItDownException)
        {
            throw new MarkItDownException($"Failed to convert EML file: {ex.Message}", ex);
        }
    }

    private async Task<string> ConvertEmailToMarkdownAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        
        // Add email headers
        result.AppendLine("# Email");
        result.AppendLine();
        
        // Essential headers
        if (!string.IsNullOrEmpty(message.Subject))
        {
            result.AppendLine($"**Subject:** {EscapeMarkdown(message.Subject)}");
        }
        
        if (message.From?.Count > 0)
        {
            result.AppendLine($"**From:** {EscapeMarkdown(string.Join(", ", message.From.Select(FormatAddress)))}");
        }
        
        if (message.To?.Count > 0)
        {
            result.AppendLine($"**To:** {EscapeMarkdown(string.Join(", ", message.To.Select(FormatAddress)))}");
        }
        
        if (message.Cc?.Count > 0)
        {
            result.AppendLine($"**CC:** {EscapeMarkdown(string.Join(", ", message.Cc.Select(FormatAddress)))}");
        }
        
        if (message.Date != DateTimeOffset.MinValue)
        {
            result.AppendLine($"**Date:** {message.Date:yyyy-MM-dd HH:mm:ss zzz}");
        }
        
        // Additional headers if present
        if (!string.IsNullOrEmpty(message.MessageId))
        {
            result.AppendLine($"**Message-ID:** {EscapeMarkdown(message.MessageId)}");
        }
        
        result.AppendLine();
        
        // Extract message body
        var bodyContent = await ExtractBodyContentAsync(message, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(bodyContent))
        {
            result.AppendLine("## Message Content");
            result.AppendLine();
            result.AppendLine(bodyContent);
            result.AppendLine();
        }
        
        // List attachments if any
        var attachments = ExtractAttachmentInfo(message);
        if (attachments.Any())
        {
            result.AppendLine("## Attachments");
            result.AppendLine();
            foreach (var attachment in attachments)
            {
                result.AppendLine($"- **{EscapeMarkdown(attachment.Name)}** ({attachment.ContentType}) - {attachment.Size}");
            }
            result.AppendLine();
        }
        
        return result.ToString().Trim();
    }

    private async Task<string> ExtractBodyContentAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        if (message.Body == null)
            return string.Empty;

        // Try to get HTML content first, then fall back to plain text
        var htmlBody = message.HtmlBody;
        if (!string.IsNullOrEmpty(htmlBody))
        {
            try
            {
                // Use our HTML converter to convert HTML to Markdown
                using var htmlStream = new MemoryStream(Encoding.UTF8.GetBytes(htmlBody));
                var htmlStreamInfo = new StreamInfo(mimeType: "text/html");
                var result = await _htmlConverter.ConvertAsync(htmlStream, htmlStreamInfo, cancellationToken).ConfigureAwait(false);
                return result.Markdown;
            }
            catch
            {
                // Fall back to plain text if HTML conversion fails
                return EscapeMarkdown(htmlBody);
            }
        }

        // Use plain text content
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
                    // Try to get size from Content-Length header or content disposition
                    if (part.ContentDisposition?.Size.HasValue == true)
                    {
                        size = FormatFileSize(part.ContentDisposition.Size.Value);
                    }
                    else if (part.Headers.Contains("Content-Length"))
                    {
                        if (long.TryParse(part.Headers["Content-Length"], out var contentLength))
                        {
                            size = FormatFileSize(contentLength);
                        }
                    }
                }
                catch
                {
                    // Keep "Unknown size" if we can't determine the size
                }
            }
            
            attachments.Add(new AttachmentInfo(name, contentType, size));
        }
        
        return attachments;
    }

    private static string FormatAddress(InternetAddress address)
    {
        return address switch
        {
            MailboxAddress mailbox when !string.IsNullOrEmpty(mailbox.Name) => 
                $"{mailbox.Name} <{mailbox.Address}>",
            MailboxAddress mailbox => mailbox.Address,
            _ => address.ToString()
        };
    }

    private static string ExtractTitle(MimeMessage message)
    {
        if (!string.IsNullOrEmpty(message.Subject))
        {
            return message.Subject.Trim();
        }
        
        // Fallback to sender information
        var sender = message.From?.FirstOrDefault();
        if (sender != null)
        {
            return $"Email from {FormatAddress(sender)}";
        }
        
        return "Email Message";
    }

    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
            
        // Escape only the most critical Markdown special characters that would break formatting
        // Be less aggressive to preserve readability, especially for email addresses
        return text
            .Replace("\\", "\\\\")  // Escape backslashes first
            .Replace("`", "\\`")    // Escape backticks
            .Replace("*", "\\*")    // Escape asterisks
            .Replace("_", "\\_");   // Escape underscores
        // Don't escape angle brackets, parentheses, and other characters in email contexts
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "bytes", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        
        return $"{len:0.##} {sizes[order]}";
    }

    private sealed record AttachmentInfo(string Name, string ContentType, string Size);
}