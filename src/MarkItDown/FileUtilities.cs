namespace MarkItDown;

/// <summary>
/// Utility class for common file operations and formatting.
/// </summary>
internal static class FileUtilities
{
    /// <summary>
    /// Formats a file size in bytes to a human-readable string with appropriate units.
    /// </summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>A formatted string with the size and appropriate unit (B, KB, MB, GB).</returns>
    public static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        
        return $"{len:0.##} {sizes[order]}";
    }
}