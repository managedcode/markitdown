using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MarkItDown.Tests;

internal static class TestAssetLoader
{
    static TestAssetLoader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static readonly string AssetsRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestFiles"));

    public static IReadOnlyDictionary<string, string> AllAssets => TestAssetCatalog.All;

    public static string AssetsDirectory => AssetsRoot;

    public static string GetAssetPath(string fileName)
    {
        var path = Path.Combine(AssetsRoot, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Fixture '{fileName}' not found at {path}.");
        }

        return path;
    }

    public static Stream OpenAsset(string fileName)
    {
        return File.OpenRead(GetAssetPath(fileName));
    }

    public static Encoding? GetEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        try
        {
            return Encoding.GetEncoding(name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
