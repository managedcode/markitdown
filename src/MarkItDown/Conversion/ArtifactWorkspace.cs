using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.Communication;
using ManagedCode.MimeTypes;
using ManagedCode.Storage.Core;
using ManagedCode.Storage.Core.Models;

namespace MarkItDown;

/// <summary>
/// Tracks a workspace directory that stores persisted conversion artifacts (images, source copies, Markdown, etc.).
/// </summary>
internal sealed class ArtifactWorkspace : IDisposable, IAsyncDisposable
{
    private readonly bool deleteOnDispose;
    private readonly IStorage? storage;
    private readonly string? storageDirectory;
    private readonly bool ownsStorage;
    private readonly object initializationLock = new();
    private int disposeState;

    private ArtifactWorkspace(
        string directoryPath,
        bool deleteOnDispose,
        bool ensureCreated,
        IStorage? storage,
        string? storageDirectory,
        bool ownsStorage)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("Directory path must be provided.", nameof(directoryPath));
        }

        DirectoryPath = directoryPath;
        this.deleteOnDispose = deleteOnDispose;
        this.storage = storage;
        this.storageDirectory = storageDirectory;
        this.ownsStorage = ownsStorage;

        if (ensureCreated)
        {
            EnsureLocalDirectoryExists();
        }
    }

    /// <summary>
    /// Gets the absolute path to the workspace directory on disk.
    /// </summary>
    public string DirectoryPath { get; }

    /// <summary>
    /// Gets the storage-relative directory used for this workspace (when backed by <see cref="IStorage"/>).
    /// </summary>
    public string? StorageDirectory => storageDirectory;

    /// <summary>
    /// Gets a value indicating whether the workspace is backed by <see cref="IStorage"/>.
    /// </summary>
    public bool IsStorageBacked => storage is not null;

    public static ArtifactWorkspace Create(string directoryPath, bool deleteOnDispose, bool ensureCreated = true)
        => new(directoryPath, deleteOnDispose, ensureCreated, storage: null, storageDirectory: null, ownsStorage: false);

    internal static ArtifactWorkspace Create(
        IStorage storage,
        string directoryPath,
        string storageDirectory,
        bool deleteOnDispose,
        bool ownsStorage,
        bool ensureCreated = true)
        => new(directoryPath, deleteOnDispose, ensureCreated, storage ?? throw new ArgumentNullException(nameof(storage)), storageDirectory, ownsStorage);

    /// <summary>
    /// Persist binary data into the workspace and return the absolute path on disk.
    /// </summary>
    public string PersistBinary(string fileName, byte[] data, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(data);

        var effectiveMime = ResolveMimeType(fileName, mimeType, MimeHelper.BIN);

        if (storage is null)
        {
            EnsureLocalDirectoryExists();
            var destination = Path.Combine(DirectoryPath, fileName);
            File.WriteAllBytes(destination, data);
            return destination;
        }

        var result = storage.UploadAsync(data, options =>
        {
            options.Directory = storageDirectory;
            options.FileName = fileName;
            options.MimeType = effectiveMime;
        }, cancellationToken).GetAwaiter().GetResult();

        var metadata = EnsureSuccess(result, "persist artifact");
        SaveLocalBinaryCopy(fileName, data);
        return ResolveStoragePath(metadata, fileName);
    }

    /// <summary>
    /// Persist text content into the workspace and return the absolute path on disk.
    /// </summary>
    public string PersistText(string fileName, string content, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var effectiveMime = ResolveMimeType(fileName, mimeType, MimeHelper.TEXT);

        if (storage is null)
        {
            EnsureLocalDirectoryExists();
            var destination = Path.Combine(DirectoryPath, fileName);
            File.WriteAllText(destination, content ?? string.Empty);
            return destination;
        }

        var payload = content ?? string.Empty;
        var result = storage.UploadAsync(payload, options =>
        {
            options.Directory = storageDirectory;
            options.FileName = fileName;
            options.MimeType = effectiveMime;
        }, cancellationToken).GetAwaiter().GetResult();

        var metadata = EnsureSuccess(result, "persist text artifact");
        SaveLocalTextCopy(fileName, payload);
        return ResolveStoragePath(metadata, fileName);
    }

    /// <summary>
    /// Copy an existing file into the workspace and return the absolute path on disk.
    /// </summary>
    public string PersistFile(string fileName, string sourcePath, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);

        var effectiveMime = ResolveMimeType(fileName, mimeType, MimeHelper.BIN);

        if (storage is null)
        {
            EnsureLocalDirectoryExists();
            var destination = Path.Combine(DirectoryPath, fileName);
            File.Copy(sourcePath, destination, overwrite: true);
            return destination;
        }

        var fileInfo = new FileInfo(sourcePath);
        var result = storage.UploadAsync(fileInfo, options =>
        {
            options.Directory = storageDirectory;
            options.FileName = fileName;
            options.MimeType = effectiveMime;
        }, cancellationToken).GetAwaiter().GetResult();

        var metadata = EnsureSuccess(result, "persist source file");
        SaveLocalFileCopy(sourcePath, fileName);
        return ResolveStoragePath(metadata, fileName);
    }

    public void Dispose()
    {
        DisposeInternal();
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        DisposeInternal();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private void DisposeInternal()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
        {
            return;
        }

        try
        {
            if (storage is not null && deleteOnDispose && !string.IsNullOrWhiteSpace(storageDirectory))
            {
                try
                {
                    var deletion = storage.DeleteDirectoryAsync(storageDirectory!, CancellationToken.None).GetAwaiter().GetResult();
                    if (!deletion.IsSuccess && deletion.Problem is not null)
                    {
                        _ = deletion.Problem.ToException();
                    }
                }
                catch
                {
                }
            }
        }
        finally
        {
            if (deleteOnDispose)
            {
                TryDeleteDirectory(DirectoryPath);
            }

            if (ownsStorage)
            {
                try
                {
                    storage?.Dispose();
                }
                catch
                {
                }
            }
        }
    }

    private void EnsureLocalDirectoryExists()
    {
        if (!IsLocalPath(DirectoryPath))
        {
            return;
        }

        if (Directory.Exists(DirectoryPath))
        {
            return;
        }

        lock (initializationLock)
        {
            if (!Directory.Exists(DirectoryPath))
            {
                Directory.CreateDirectory(DirectoryPath);
            }
        }
    }

    private static BlobMetadata EnsureSuccess(Result<BlobMetadata> result, string operation)
    {
        if (result.IsSuccess)
        {
            return result.Value;
        }

        if (result.Problem is not null)
        {
            throw result.Problem.ToException();
        }

        throw new InvalidOperationException($"Failed to {operation}.");
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (!IsLocalPath(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

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

    private void SaveLocalBinaryCopy(string fileName, byte[] data)
    {
        if (!IsLocalPath(DirectoryPath))
        {
            return;
        }

        try
        {
            EnsureLocalDirectoryExists();
            var destination = Path.Combine(DirectoryPath, fileName);
            File.WriteAllBytes(destination, data);
        }
        catch
        {
        }
    }

    private void SaveLocalTextCopy(string fileName, string content)
    {
        if (!IsLocalPath(DirectoryPath))
        {
            return;
        }

        try
        {
            EnsureLocalDirectoryExists();
            var destination = Path.Combine(DirectoryPath, fileName);
            File.WriteAllText(destination, content);
        }
        catch
        {
        }
    }

    private void SaveLocalFileCopy(string sourcePath, string fileName)
    {
        if (!IsLocalPath(DirectoryPath))
        {
            return;
        }

        try
        {
            EnsureLocalDirectoryExists();
            var destination = Path.Combine(DirectoryPath, fileName);
            File.Copy(sourcePath, destination, overwrite: true);
        }
        catch
        {
        }
    }

    private string ResolveStoragePath(BlobMetadata metadata, string fallbackFileName)
    {
        if (metadata?.Uri is Uri uri)
        {
            return uri.ToString();
        }

        if (!string.IsNullOrWhiteSpace(metadata?.FullName))
        {
            if (!string.IsNullOrWhiteSpace(storageDirectory))
            {
                return CombineStoragePath(storageDirectory!, metadata!.FullName);
            }

            return metadata!.FullName;
        }

        return Path.Combine(DirectoryPath, fallbackFileName);
    }

    private static string CombineStoragePath(string basePath, string relative)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return relative;
        }

        if (basePath.EndsWith("/", StringComparison.Ordinal))
        {
            return $"{basePath}{relative}";
        }

        return $"{basePath}/{relative}";
    }

    private static string ResolveMimeType(string fileName, string? candidate, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }

        var inferred = MimeHelper.GetMimeType(fileName);
        if (!string.IsNullOrWhiteSpace(inferred))
        {
            return inferred!;
        }

        return fallback;
    }
}
