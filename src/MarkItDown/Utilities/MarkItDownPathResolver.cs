using System;
using System.IO;

namespace MarkItDown;

/// <summary>
/// Provides centralized helpers for resolving MarkItDown's working directories.
/// </summary>
internal static class MarkItDownPathResolver
{
    private static readonly object _gate = new();
    private static string? _configuredRootPath;
    private static string? _resolvedRootPath;

    /// <summary>
    /// Gets the absolute root directory used for MarkItDown workspaces.
    /// Thread-safe; the value is resolved on first access and cached.
    /// </summary>
    public static string RootPath
    {
        get
        {
            if (_resolvedRootPath is not null)
            {
                return _resolvedRootPath;
            }

            lock (_gate)
            {
                _resolvedRootPath ??= CreateRootPath();
                return _resolvedRootPath;
            }
        }
    }

    /// <summary>
    /// Override the default root directory.
    /// Must be called before any code accesses <see cref="RootPath"/> (typically
    /// by setting <c>MarkItDownOptions.RootPath</c> before constructing a client).
    /// Throws if the root has already resolved to a different path.
    /// </summary>
    internal static void Configure(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var normalized = Path.GetFullPath(rootPath);

        lock (_gate)
        {
            // Already resolved -- only allow if it matches.
            if (_resolvedRootPath is not null)
            {
                if (!PathEquals(_resolvedRootPath, normalized))
                {
                    throw new InvalidOperationException(
                        $"Root already resolved to '{_resolvedRootPath}'; cannot change to '{normalized}'.");
                }

                return;
            }

            // Not yet resolved -- only allow if no prior Configure set a different path.
            if (_configuredRootPath is not null && !PathEquals(_configuredRootPath, normalized))
            {
                throw new InvalidOperationException(
                    $"Root already configured as '{_configuredRootPath}'; cannot change to '{normalized}'.");
            }

            _configuredRootPath = normalized;
        }
    }

    /// <summary>
    /// Ensure the root directory exists (also invoked by lazy initialization).
    /// </summary>
    public static void EnsureRootExists()
    {
        _ = RootPath;
    }

    /// <summary>
    /// Combine the root directory with additional segments.
    /// </summary>
    public static string Combine(params string[] segments)
    {
        if (segments is null || segments.Length == 0)
        {
            return RootPath;
        }

        var paths = new string[segments.Length + 1];
        paths[0] = RootPath;

        for (var i = 0; i < segments.Length; i++)
        {
            paths[i + 1] = segments[i];
        }

        return Path.Combine(paths);
    }

    /// <summary>
    /// Combine the root directory with segments and ensure the resulting directory exists.
    /// </summary>
    public static string Ensure(params string[] segments)
    {
        var path = Combine(segments);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateRootPath()
    {
        var candidate = _configuredRootPath
            ?? Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".markitdown"));
        Directory.CreateDirectory(candidate);
        return candidate;
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a),
            Path.GetFullPath(b),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
