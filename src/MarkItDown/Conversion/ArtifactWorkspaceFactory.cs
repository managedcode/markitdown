using System;
using System.IO;
using System.Text;

namespace MarkItDown;

/// <summary>
/// Creates <see cref="ArtifactWorkspace"/> instances based on configured storage options.
/// </summary>
internal static class ArtifactWorkspaceFactory
{
    public static ArtifactWorkspace CreateWorkspace(StreamInfo streamInfo, SegmentOptions segmentOptions, ArtifactStorageOptions storageOptions)
    {
        ArgumentNullException.ThrowIfNull(streamInfo);
        segmentOptions ??= SegmentOptions.Default;
        storageOptions ??= ArtifactStorageOptions.Default;

        var explicitDirectory = segmentOptions.Image.ArtifactDirectory;
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            var directory = Path.GetFullPath(explicitDirectory);
            Directory.CreateDirectory(directory);
            return ArtifactWorkspace.Create(directory, deleteOnDispose: false);
        }

        var storageFactory = storageOptions.StorageFactory;
        if (storageFactory is null)
        {
            var localDirectory = BuildLocalWorkspacePath(streamInfo, segmentOptions.Image);
            var deleteOnDispose = storageOptions.DeleteOnDispose && !segmentOptions.Image.KeepArtifactDirectory;
            return ArtifactWorkspace.Create(localDirectory, deleteOnDispose, ensureCreated: true);
        }

        var workspaceName = GenerateWorkspaceName(streamInfo, storageOptions);
        var storageDirectory = NormalizeStoragePath(workspaceName);
        var directoryPath = storageOptions.WorkspacePathFormatter?.Invoke(workspaceName)
            ?? MarkItDownPathResolver.Combine("artifacts", workspaceName);
        var deleteOnDisposeStorage = storageOptions.DeleteOnDispose && !segmentOptions.Image.KeepArtifactDirectory;
        var ownsStorage = storageOptions.DisposeStorage;
        var ensureCreated = IsLocalPath(directoryPath);

        var storage = storageFactory();
        try
        {
            return ArtifactWorkspace.Create(storage, directoryPath, storageDirectory, deleteOnDisposeStorage, ownsStorage, ensureCreated);
        }
        catch
        {
            if (ownsStorage)
            {
                storage.Dispose();
            }

            throw;
        }
    }

    private static string BuildLocalWorkspacePath(StreamInfo streamInfo, ImageSegmentOptions imageOptions)
    {
        var scope = streamInfo.FileName ?? streamInfo.LocalPath ?? streamInfo.Url ?? "document";
        var sanitized = Sanitize(Path.GetFileNameWithoutExtension(scope));
        if (imageOptions.KeepArtifactDirectory)
        {
            return MarkItDownPathResolver.Ensure("artifacts", sanitized, Guid.NewGuid().ToString("N"));
        }

        var artifactsRoot = MarkItDownPathResolver.Ensure("artifacts", sanitized);
        return Path.Combine(artifactsRoot, Path.GetRandomFileName());
    }

    private static string GenerateWorkspaceName(StreamInfo streamInfo, ArtifactStorageOptions storageOptions)
    {
        if (storageOptions.WorkspaceNameGenerator is not null)
        {
            var custom = storageOptions.WorkspaceNameGenerator(streamInfo);
            if (!string.IsNullOrWhiteSpace(custom))
            {
                return Sanitize(custom);
            }
        }

        var candidate = streamInfo.FileName ?? streamInfo.LocalPath ?? streamInfo.Url ?? "document";
        var baseName = Sanitize(Path.GetFileNameWithoutExtension(candidate));
        var suffix = Guid.NewGuid().ToString("N")[..12];
        return $"{baseName}-{suffix}";
    }

    private static string NormalizeStoragePath(string path)
        => path.Replace('\\', '/');

    private static bool IsLocalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsAbsoluteUri && uri.Scheme != Uri.UriSchemeFile)
        {
            return false;
        }

        return Path.IsPathRooted(path);
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "document";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (ch is '-' or '_')
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var sanitized = builder.ToString().Trim('-');
        return sanitized.Length == 0 ? "document" : sanitized;
    }
}
