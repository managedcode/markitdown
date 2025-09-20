using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MarkItDown.Core.Converters;

internal static class ExifToolMetadataExtractor
{
    public static async Task<Dictionary<string, string>> ExtractAsync(byte[] data, string? extension, string? overridePath, CancellationToken cancellationToken)
    {
        var path = ResolveExifToolPath(overridePath);
        if (path is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var tempFile = CreateTempImagePath(extension);
        await File.WriteAllBytesAsync(tempFile, data, cancellationToken).ConfigureAwait(false);

        try
        {
            var output = await RunProcessAsync(path, ["-json", tempFile], cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var element = root[0];
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                var value = property.Value.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result[property.Name] = value;
                }
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    private static string? ResolveExifToolPath(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var envPath = Environment.GetEnvironmentVariable("EXIFTOOL_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        return FindOnPath("exiftool");
    }

    private static string CreateTempImagePath(string? extension)
    {
        var ext = string.IsNullOrWhiteSpace(extension) ? ".tmp" : (extension.StartsWith('.') ? extension : "." + extension);
        var fileName = $"markitdown-{Guid.NewGuid():N}{ext}";
        return Path.Combine(Path.GetTempPath(), fileName);
    }

    private static async Task<string> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var outputBuilder = new StringBuilder();

        var outputCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                outputCompletion.TrySetResult(true);
            }
            else
            {
                outputBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        var waitForExit = process.WaitForExitAsync(cancellationToken);

        await Task.WhenAll(waitForExit, outputCompletion.Task).ConfigureAwait(false);

        return outputBuilder.ToString();
    }

    private static string? FindOnPath(string toolName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
        {
            return null;
        }

        foreach (var segment in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = segment.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            var candidate = Path.Combine(trimmed, toolName);
            if (OperatingSystem.IsWindows())
            {
                var exePath = candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? candidate : candidate + ".exe";
                if (File.Exists(exePath))
                {
                    return exePath;
                }
            }
            else if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Intentionally ignored
        }
    }
}
