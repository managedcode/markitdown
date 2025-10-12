using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarkItDown.Conversion.Middleware;

/// <summary>
/// Middleware that enriches extracted images using an <see cref="IChatClient"/>.
/// </summary>
public sealed class AiImageEnrichmentMiddleware : IConversionMiddleware
{
    public async Task InvokeAsync(ConversionPipelineContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.Artifacts.Images.Count == 0)
        {
            return;
        }

        var images = context.Artifacts.Images;
        var pending = new List<ImageArtifact>(images.Count);

        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (image.Metadata.TryGetValue(MetadataKeys.ImageEnriched, out var enrichedFlag) &&
                bool.TryParse(enrichedFlag, out var alreadyEnriched) && alreadyEnriched)
            {
                continue;
            }

            pending.Add(image);
        }

        if (pending.Count == 0)
        {
            return;
        }

        var chatCallback = CreateChatCallback(context);
        if (chatCallback is null)
        {
            return;
        }

        var maxParallel = context.SegmentOptions.MaxParallelImageAnalysis;
        if (maxParallel <= 0)
        {
            throw new InvalidOperationException("SegmentOptions.MaxParallelImageAnalysis must be positive.");
        }

        var throttler = new SemaphoreSlim(maxParallel);

        var results = new ImageChatEnrichmentResult?[pending.Count];
        var tasks = new Task[pending.Count];
        var conversionContext = ConversionContextAccessor.Current;
        var progress = conversionContext?.Progress;
        var progressDetail = conversionContext?.ProgressDetail ?? ProgressDetailLevel.Basic;

        try
        {
            for (var i = 0; i < pending.Count; i++)
            {
                var index = i;
                var artifact = pending[index];
                tasks[index] = ProcessImageAsync(index, artifact, throttler, results, chatCallback, context.Logger, cancellationToken);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            throttler.Dispose();
        }

        var processedCount = 0;
        var enrichedCount = 0;
        var totalTokens = 0;

        for (var i = 0; i < results.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var enrichment = results[i];
            if (enrichment is null)
            {
                throw new InvalidOperationException("Image enrichment returned no result.");
            }

            if (!enrichment.HasInsight)
            {
                processedCount++;
                totalTokens += enrichment.Usage.TotalTokens;
                if (progress is not null && progressDetail == ProgressDetailLevel.Detailed)
                {
                    var currentImage = pending[i];
                    var details = BuildEnrichmentDetails(currentImage, enrichment, totalTokens);
                    progress.Report(new ConversionProgress("image-enrichment", processedCount, pending.Count, details));
                }

                continue;
            }

            var image = pending[i];
            if (!TryBuildInsightBlock(image, out var block))
            {
                block = MetaMarkdownFormatter.BuildImageComment(image);
                if (string.IsNullOrWhiteSpace(block) || ContainsFallbackComment(block))
                {
                    var description = ResolveDescription(image);
                    block = string.IsNullOrWhiteSpace(description)
                        ? null
                        : BuildBasicComment(description!, image.RawText);
                }

                if (string.IsNullOrWhiteSpace(block))
                {
                    continue;
                }
            }

            UpdateSegments(context, image, block);

            enrichedCount++;
            processedCount++;
            totalTokens += enrichment.Usage.TotalTokens;

            if (progress is not null && progressDetail == ProgressDetailLevel.Detailed)
            {
                var details = BuildEnrichmentDetails(image, enrichment, totalTokens);
                progress.Report(new ConversionProgress("image-enrichment", processedCount, pending.Count, details));
            }
        }

        if (progress is not null)
        {
            var summary = $"images={enrichedCount}/{pending.Count} tokens={totalTokens}";
            progress.Report(new ConversionProgress("image-enrichment", processedCount, pending.Count, summary));
        }
    }

    private static string BuildEnrichmentDetails(ImageArtifact image, ImageChatEnrichmentResult enrichment, int cumulativeTokens)
    {
        string page;
        if (image.PageNumber.HasValue)
        {
            page = image.PageNumber.Value.ToString(CultureInfo.InvariantCulture);
        }
        else if (image.Metadata.TryGetValue(MetadataKeys.Page, out var rawPage) && !string.IsNullOrWhiteSpace(rawPage))
        {
            page = rawPage.Trim();
        }
        else
        {
            page = "?";
        }

        return $"page={page} tokens={enrichment.Usage.TotalTokens} cumulative={cumulativeTokens}";
    }

    private static Func<ImageArtifact, CancellationToken, Task<ImageChatEnrichmentResult?>>? CreateChatCallback(ConversionPipelineContext context)
    {
        if (!context.SegmentOptions.Image.EnableAiEnrichment)
        {
            return null;
        }

        var chatClient = context.AiModels.ChatClient;
        if (chatClient is null)
        {
            return null;
        }

        return (artifact, cancellationToken) =>
            ImageChatEnricher.EnrichAsync(artifact, context.StreamInfo, chatClient, context.Logger, cancellationToken);
    }

    private static async Task ProcessImageAsync(
        int index,
        ImageArtifact artifact,
        SemaphoreSlim throttler,
        ImageChatEnrichmentResult?[] results,
        Func<ImageArtifact, CancellationToken, Task<ImageChatEnrichmentResult?>> chatCallback,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            results[index] = await chatCallback(artifact, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Image enrichment failed for {Label}", artifact.Label ?? artifact.PageNumber?.ToString(CultureInfo.InvariantCulture));
            throw;
        }
        finally
        {
            throttler.Release();
        }
    }

    private static void UpdateSegments(ConversionPipelineContext context, ImageArtifact image, string block)
    {
        var oldPlaceholder = image.PlaceholderMarkdown;
        var newPlaceholder = BuildImagePlaceholder(image);
        image.PlaceholderMarkdown = newPlaceholder;

        if (image.SegmentIndex is int index && index >= 0 && index < context.Segments.Count)
        {
            var existing = context.Segments[index];
            var metadata = new Dictionary<string, string>(existing.AdditionalMetadata)
            {
                [MetadataKeys.ImageEnriched] = bool.TrueString
            };

            if (image.PageNumber is int page && !metadata.ContainsKey(MetadataKeys.Page))
            {
                metadata[MetadataKeys.Page] = page.ToString(CultureInfo.InvariantCulture);
            }

            var updatedMarkdown = existing.Markdown;

            if (!string.IsNullOrWhiteSpace(oldPlaceholder) &&
                !string.Equals(oldPlaceholder, newPlaceholder, StringComparison.Ordinal))
            {
                updatedMarkdown = updatedMarkdown.Replace(oldPlaceholder!, newPlaceholder);
            }

            var replaced = ReplaceImageBlock(updatedMarkdown, oldPlaceholder, newPlaceholder, block);
            if (replaced is null)
            {
                var builder = new StringBuilder(updatedMarkdown);
                if (builder.Length > 0)
                {
                    if (!updatedMarkdown.EndsWith("\n", StringComparison.Ordinal))
                    {
                        builder.AppendLine();
                    }

                    builder.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(newPlaceholder))
                {
                    builder.AppendLine(newPlaceholder);
                    builder.AppendLine();
                }

                builder.AppendLine(block);
                updatedMarkdown = builder.ToString();
            }
            else
            {
                updatedMarkdown = replaced;
            }
            context.Segments[index] = new DocumentSegment(
                updatedMarkdown.TrimEnd(),
                existing.Type,
                existing.Number,
                existing.Label,
                existing.StartTime,
                existing.EndTime,
                existing.Source,
                metadata);
        }
        else
        {
            var metadata = new Dictionary<string, string>
            {
                [MetadataKeys.ImageEnriched] = bool.TrueString
            };

            if (image.PageNumber is int page)
            {
                metadata[MetadataKeys.Page] = page.ToString(CultureInfo.InvariantCulture);
            }

            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(newPlaceholder))
            {
                builder.AppendLine(newPlaceholder);
                builder.AppendLine();
            }

            builder.AppendLine(block);
            var content = builder.ToString().TrimEnd();

            context.Segments.Add(new DocumentSegment(
                content,
                SegmentType.Image,
                image.PageNumber,
                image.Label,
                source: image.Source,
                additionalMetadata: metadata));
            image.SegmentIndex = context.Segments.Count - 1;
        }
    }



    private static string BuildImagePlaceholder(ImageArtifact image)
    {
        string? summary = image.DetailedDescription;

        if (string.IsNullOrWhiteSpace(summary) &&
            image.Metadata.TryGetValue(MetadataKeys.DetailedDescription, out var detailed) &&
            !string.IsNullOrWhiteSpace(detailed))
        {
            summary = detailed;
        }

        if (string.IsNullOrWhiteSpace(summary) &&
            image.Metadata.TryGetValue(MetadataKeys.Caption, out var caption) &&
            !string.IsNullOrWhiteSpace(caption))
        {
            summary = caption;
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = image.Label;
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = image.RawText;
        }

        summary = TextSanitizer.Normalize(summary, trim: true);
        var contextLabel = image.PageNumber.HasValue
            ? $"Image (page {image.PageNumber.Value})"
            : "Image";

        return ImagePlaceholderFormatter.BuildPlaceholder(image, summary, contextLabel);
    }

    private static bool TryInjectAfterPlaceholder(string existingMarkdown, string placeholder, string enrichmentBlock, out string updated)
    {
        var index = existingMarkdown.IndexOf(placeholder, StringComparison.Ordinal);
        if (index < 0)
        {
            updated = existingMarkdown;
            return false;
        }

        var insertPosition = index + placeholder.Length;
        var builder = new StringBuilder(existingMarkdown.Length + enrichmentBlock.Length + 2);
        builder.Append(existingMarkdown, 0, insertPosition);

        var hasImmediateNewline = insertPosition < existingMarkdown.Length && existingMarkdown[insertPosition] == '\n';
        if (!hasImmediateNewline)
        {
            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine(enrichmentBlock);
        builder.Append(existingMarkdown.AsSpan(insertPosition));
        updated = builder.ToString();
        return true;
    }

    private static string? ReplaceImageBlock(string existingMarkdown, string? oldPlaceholder, string newPlaceholder, string block)
    {
        if (string.IsNullOrWhiteSpace(newPlaceholder))
        {
            return null;
        }

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(oldPlaceholder))
        {
            candidates.Add(oldPlaceholder!);
        }

        if (!string.IsNullOrWhiteSpace(newPlaceholder))
        {
            candidates.Add(newPlaceholder);
        }

        var fallbackPrefixPattern = "(?:<!-- Image description:[\\s\\S]*?(AI enrichment not available|Image captured without AI enrichment)[\\s\\S]*?-->\\s*)?";
        var trailingImageCommentPattern = "(?:\\s*\\n\\s*<!-- Image description:[\\s\\S]*?-->)*";
        var replacement = $"{newPlaceholder}\n\n{block}";

        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var placeholderPattern = Regex.Escape(candidate);
            var pattern = $"{fallbackPrefixPattern}{placeholderPattern}{trailingImageCommentPattern}";

            var regex = new Regex(pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var updated = regex.Replace(existingMarkdown, replacement, 1);
            if (!ReferenceEquals(updated, existingMarkdown))
            {
                return updated;
            }
        }

        var fallbackOnlyPattern = "<!-- Image description:[\\s\\S]*?(AI enrichment not available|Image captured without AI enrichment)[\\s\\S]*?-->\\s*";
        var scrubbed = Regex.Replace(existingMarkdown, fallbackOnlyPattern, string.Empty, RegexOptions.IgnoreCase);

        if (TryInjectAfterPlaceholder(scrubbed, newPlaceholder, block, out var injectedNew))
        {
            return injectedNew;
        }

        if (!string.IsNullOrWhiteSpace(oldPlaceholder) &&
            TryInjectAfterPlaceholder(scrubbed, oldPlaceholder!, block, out var injectedOld))
        {
            return injectedOld.Replace(oldPlaceholder!, newPlaceholder);
        }

        return null;
    }

    private static bool ContainsFallbackComment(string comment)
        => comment.Contains("AI enrichment not available", StringComparison.OrdinalIgnoreCase) ||
           comment.Contains("Image captured without AI enrichment", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveDescription(ImageArtifact image)
    {
        if (!string.IsNullOrWhiteSpace(image.DetailedDescription))
        {
            return image.DetailedDescription;
        }

        if (image.Metadata.TryGetValue(MetadataKeys.DetailedDescription, out var detailed) && !string.IsNullOrWhiteSpace(detailed))
        {
            return detailed;
        }

        if (image.Metadata.TryGetValue(MetadataKeys.Caption, out var caption) && !string.IsNullOrWhiteSpace(caption))
        {
            return caption;
        }

        if (!string.IsNullOrWhiteSpace(image.Label))
        {
            return image.Label;
        }

        if (image.Metadata.TryGetValue(MetadataKeys.OcrText, out var ocr) && !string.IsNullOrWhiteSpace(ocr))
        {
            return ocr;
        }

        if (!string.IsNullOrWhiteSpace(image.RawText))
        {
            return image.RawText;
        }

        return null;
    }

    private static string BuildBasicComment(string description, string? ocr)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!-- Image description:");
        builder.AppendLine(description.Trim());

        if (!string.IsNullOrWhiteSpace(ocr))
        {
            builder.AppendLine();
            builder.AppendLine("Visible text:");
            foreach (var line in ocr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                builder.Append("- ").AppendLine(line);
            }
        }

        builder.Append("-->");
        return builder.ToString();
    }

    private static bool TryBuildInsightBlock(ImageArtifact image, out string block)
    {
        block = string.Empty;

        if (!image.Metadata.TryGetValue(MetadataKeys.ImageInsight, out var json) || string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var insight = JsonSerializer.Deserialize<ImageChatInsight>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (insight is null)
            {
                return false;
            }

            var markdown = insight.ToMarkdown();
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return false;
            }

            block = markdown.Trim();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
