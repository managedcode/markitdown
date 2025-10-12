using System;
using ManagedCode.Storage.Core;

namespace MarkItDown;

/// <summary>
/// Configures how MarkItDown allocates storage for per-document workspaces and artifacts.
/// </summary>
public sealed record ArtifactStorageOptions
{
    /// <summary>
    /// Shared default instance.
    /// </summary>
    public static ArtifactStorageOptions Default { get; } = new();

    /// <summary>
    /// Factory that produces a fresh <see cref="IStorage"/> implementation for each conversion workspace.
    /// When <see langword="null"/>, MarkItDown falls back to local disk persistence.
    /// </summary>
    public Func<IStorage>? StorageFactory { get; init; }

    /// <summary>
    /// Optional hook that generates the base workspace folder name for a given <see cref="StreamInfo"/>.
    /// </summary>
    public Func<StreamInfo, string>? WorkspaceNameGenerator { get; init; }

    /// <summary>
    /// Optional formatter that maps the generated workspace name to a directory path reported to consumers.
    /// </summary>
    public Func<string, string>? WorkspacePathFormatter { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the original source document is copied into the workspace.
    /// </summary>
    public bool CopySourceDocument { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/>, the composed Markdown is written to the workspace after conversion completes.
    /// </summary>
    public bool PersistMarkdown { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/>, the workspace directory is deleted once the conversion result is disposed.
    /// </summary>
    public bool DeleteOnDispose { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/>, the storage instance created for a workspace is disposed alongside it.
    /// </summary>
    public bool DisposeStorage { get; init; } = true;
}
