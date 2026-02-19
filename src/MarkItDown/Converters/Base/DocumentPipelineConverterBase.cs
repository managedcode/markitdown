using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MarkItDown.Converters;

/// <summary>
/// Base converter aligned with the documented disk-first pipeline. It persists incoming streams to a workspace and exposes helpers for re-opening the file.
/// </summary>
public abstract class DocumentPipelineConverterBase : DocumentConverterBase
{
    protected DocumentPipelineConverterBase(int priority)
        : base(priority)
    {
    }

    /// <summary>
    /// Persist the incoming <paramref name="sourceStream"/> to disk (when required) and return a scoped handle to the materialised file.
    /// </summary>
    protected static async ValueTask<DocumentPipelineSourceScope> MaterializeSourceAsync(
        Stream sourceStream,
        StreamInfo streamInfo,
        string defaultExtension,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceStream);
        ArgumentNullException.ThrowIfNull(streamInfo);

        if (!string.IsNullOrWhiteSpace(streamInfo.LocalPath) && File.Exists(streamInfo.LocalPath))
        {
            return DocumentPipelineSourceScope.ForExisting(streamInfo.LocalPath);
        }

        if (sourceStream is FileStream fileStream &&
            !string.IsNullOrWhiteSpace(fileStream.Name) &&
            File.Exists(fileStream.Name))
        {
            return DocumentPipelineSourceScope.ForExisting(fileStream.Name);
        }

        var workspaceRoot = CreateWorkspaceRoot();
        Directory.CreateDirectory(workspaceRoot);

        var fileName = ResolveOutputFileName(streamInfo, defaultExtension);
        var destinationPath = Path.Combine(workspaceRoot, fileName);

        long? originalPosition = null;

        try
        {
            if (sourceStream.CanSeek)
            {
                originalPosition = sourceStream.Position;
                sourceStream.Position = 0;
            }

            var options = new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.Create,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            };

            await using (var writer = new FileStream(destinationPath, options))
            {
                await sourceStream.CopyToAsync(writer, cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            DocumentPipelineSourceScope.DeleteFile(destinationPath);
            DocumentPipelineSourceScope.DeleteDirectory(workspaceRoot);
            throw;
        }
        finally
        {
            if (sourceStream.CanSeek)
            {
                if (originalPosition.HasValue)
                {
                    sourceStream.Position = originalPosition.Value;
                }
                else
                {
                    sourceStream.Position = 0;
                }
            }
        }

        return DocumentPipelineSourceScope.ForWorkspaceFile(destinationPath, workspaceRoot);
    }

    /// <summary>
    /// Open the file at <paramref name="path"/> for shared read-only access with pipeline-friendly options.
    /// </summary>
    protected static FileStream OpenReadOnlyFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must be provided.", nameof(path));
        }

        var options = new FileStreamOptions
        {
            Access = FileAccess.Read,
            Mode = FileMode.Open,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };

        return new FileStream(path, options);
    }

    private static string ResolveOutputFileName(StreamInfo streamInfo, string defaultExtension)
    {
        if (!string.IsNullOrWhiteSpace(streamInfo.FileName))
        {
            return Path.GetFileName(streamInfo.FileName);
        }

        var extension = streamInfo.Extension;
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = defaultExtension;
        }
        else if (!extension.StartsWith('.'))
        {
            extension = $".{extension}";
        }

        return $"source{extension}";
    }

    private static string CreateWorkspaceRoot()
    {
        var baseRoot = MarkItDownPathResolver.Ensure("workspace");
        var unique = Guid.NewGuid().ToString("N");
        return Path.Combine(baseRoot, unique);
    }
}

/// <summary>
/// Scoped handle to the persisted file used by the document pipeline.
/// </summary>
public sealed class DocumentPipelineSourceScope : IAsyncDisposable
{
    private readonly string? workspaceRoot;
    private readonly bool ownsWorkspace;

    private DocumentPipelineSourceScope(string filePath, string? workspaceRoot, bool ownsWorkspace)
    {
        FilePath = filePath;
        this.workspaceRoot = workspaceRoot;
        this.ownsWorkspace = ownsWorkspace;
    }

    public string FilePath { get; }

    internal static DocumentPipelineSourceScope ForExisting(string filePath)
        => new DocumentPipelineSourceScope(filePath, workspaceRoot: null, ownsWorkspace: false);

    internal static DocumentPipelineSourceScope ForWorkspaceFile(string filePath, string workspaceRoot)
        => new DocumentPipelineSourceScope(filePath, workspaceRoot, ownsWorkspace: true);

    internal static void DeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    internal static void DeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
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

    public ValueTask DisposeAsync()
    {
        if (ownsWorkspace)
        {
            DeleteFile(FilePath);
            DeleteDirectory(workspaceRoot);
        }

        return ValueTask.CompletedTask;
    }
}
