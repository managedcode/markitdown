using System;
using System.IO;

namespace MarkItDown;

/// <summary>
/// Provides centralized helpers for resolving MarkItDown's working directories.
/// </summary>
internal static class MarkItDownPathResolver
{
    private static readonly Lazy<string> root = new(CreateRootPath, isThreadSafe: true);

    /// <summary>
    /// Gets the absolute root directory used for MarkItDown workspaces.
    /// </summary>
    public static string RootPath => root.Value;

    /// <summary>
    /// Ensure the root directory exists (also invoked by lazy initialization).
    /// </summary>
    public static void EnsureRootExists()
    {
        _ = root.Value;
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
        var candidate = Path.Combine(Environment.CurrentDirectory, ".markitdown");
        Directory.CreateDirectory(candidate);
        return candidate;
    }
}
