using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;

namespace MarkItDown.Cli;

internal sealed class ConversionService
{
    public static async Task<ConversionSummary> ConvertFilesAsync(IReadOnlyList<string>? files, string outputDirectory, MarkItDownOptions options, IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (files is null || files.Count == 0)
        {
            return new ConversionSummary(Array.Empty<ConversionResult>());
        }

        Directory.CreateDirectory(outputDirectory);
        var results = new List<ConversionResult>(files.Count);
        var markItDown = new MarkItDownClient(options);

        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            progress?.Report(new ConversionProgress(index, files.Count, file));

            try
            {
                await using var conversion = await markItDown.ConvertAsync(file, cancellationToken).ConfigureAwait(false);
                var markdown = conversion.Markdown;
                var outputPath = await WriteMarkdownAsync(markdown, file, outputDirectory, cancellationToken).ConfigureAwait(false);
                results.Add(CreateSuccessResult(conversion, file, outputPath));
            }
            catch (Exception ex)
            {
                results.Add(new ConversionResult(file, null, false, ex.Message, 0, null, 0, 0, 0, 0, null));
            }

            progress?.Report(new ConversionProgress(index + 1, files.Count, file));
        }

        return new ConversionSummary(results);
    }

    public static async Task<ConversionSummary> ConvertUrlAsync(string url, string outputDirectory, MarkItDownOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL is required", nameof(url));
        }

        Directory.CreateDirectory(outputDirectory);
        var markItDown = new MarkItDownClient(options);
        await using var conversion = await markItDown.ConvertFromUrlAsync(url, cancellationToken: cancellationToken).ConfigureAwait(false);
        var markdown = conversion.Markdown;
        var fileName = DeriveFileNameFromUrl(url, ResolveTitle(conversion));
        var outputPath = await WriteMarkdownAsync(markdown, fileName, outputDirectory, cancellationToken).ConfigureAwait(false);
        var result = CreateSuccessResult(conversion, url, outputPath);
        return new ConversionSummary(new[] { result });
    }

    private static string DeriveFileNameFromUrl(string url, string? title)
    {
        var baseName = !string.IsNullOrWhiteSpace(title) ? title : url;
        var sanitized = new string(baseName.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray());
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "document";
        }

        return Path.ChangeExtension(sanitized, ".md");
    }

    private static async Task<string> WriteMarkdownAsync(string? markdown, string inputPath, string outputDirectory, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileNameWithoutExtension(inputPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "document";
        }

        var basePath = Path.Combine(outputDirectory, fileName);
        var path = basePath + ".md";
        var counter = 1;
        while (File.Exists(path))
        {
            path = $"{basePath}_{counter}.md";
            counter++;
        }

        await File.WriteAllTextAsync(path, markdown ?? string.Empty, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static ConversionResult CreateSuccessResult(DocumentConverterResult conversion, string input, string outputPath)
    {
        var metadata = conversion.Metadata ?? new Dictionary<string, string>();
        var title = ResolveTitle(conversion);
        var pageCount = ParseCount(metadata, MetadataKeys.DocumentPages);
        var imageCount = conversion.Artifacts?.Images?.Count ?? 0;
        var tableCount = conversion.Artifacts?.Tables?.Count ?? 0;
        var attachmentCount = ParseCount(metadata, MetadataKeys.EmailAttachmentsCount);
        var attachmentSummary = TryGetValue(metadata, MetadataKeys.EmailAttachments);

        return new ConversionResult(
            input,
            outputPath,
            true,
            null,
            conversion.Segments.Count,
            title,
            pageCount,
            imageCount,
            tableCount,
            attachmentCount,
            string.IsNullOrWhiteSpace(attachmentSummary) ? null : attachmentSummary);
    }

    private static string? ResolveTitle(DocumentConverterResult conversion)
    {
        if (!string.IsNullOrWhiteSpace(conversion.Title))
        {
            return conversion.Title;
        }

        if (conversion.Metadata.TryGetValue(MetadataKeys.DocumentTitle, out var title) && !string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        return null;
    }

    private static int ParseCount(IReadOnlyDictionary<string, string> metadata, string key)
    {
        if (metadata.TryGetValue(key, out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed >= 0)
        {
            return parsed;
        }

        return 0;
    }

    private static string? TryGetValue(IReadOnlyDictionary<string, string> metadata, string key)
        => metadata.TryGetValue(key, out var value) ? value : null;
}
