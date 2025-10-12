using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;

namespace MarkItDown.Cli;

internal sealed class ConversionService
{
    public async Task<ConversionSummary> ConvertFilesAsync(IReadOnlyList<string> files, string outputDirectory, MarkItDownOptions options, IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default)
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
                var conversion = await markItDown.ConvertAsync(file, cancellationToken).ConfigureAwait(false);
                var outputPath = await WriteMarkdownAsync(conversion.Markdown, file, outputDirectory, cancellationToken).ConfigureAwait(false);
                results.Add(new ConversionResult(file, outputPath, true, null, conversion.Segments.Count));
            }
            catch (Exception ex)
            {
                results.Add(new ConversionResult(file, null, false, ex.Message, 0));
            }

            progress?.Report(new ConversionProgress(index + 1, files.Count, file));
        }

        return new ConversionSummary(results);
    }

    public async Task<ConversionSummary> ConvertUrlAsync(string url, string outputDirectory, MarkItDownOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL is required", nameof(url));
        }

        Directory.CreateDirectory(outputDirectory);
        var markItDown = new MarkItDownClient(options);
        var conversion = await markItDown.ConvertFromUrlAsync(url, cancellationToken: cancellationToken).ConfigureAwait(false);
        var outputPath = await WriteMarkdownAsync(conversion.Markdown, DeriveFileNameFromUrl(url, conversion.Title), outputDirectory, cancellationToken).ConfigureAwait(false);
        var result = new ConversionResult(url, outputPath, true, null, conversion.Segments.Count);
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
}
