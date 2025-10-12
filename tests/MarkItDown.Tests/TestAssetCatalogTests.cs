using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shouldly;
using Xunit;

namespace MarkItDown.Tests;

public class TestAssetCatalogTests
{
    [Fact]
    public void AllAssets_EnumeratesEveryFixtureFile()
    {
        var assetRoot = TestAssetLoader.AssetsDirectory;
        var normalizedRoot = assetRoot.Replace('\\', '/').TrimEnd('/');

        var expected = Directory.GetFiles(assetRoot, "*", SearchOption.AllDirectories)
            .Select(path => path.Replace('\\', '/'))
            .Where(path => !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal))
            .Select(path => path.Substring(normalizedRoot.Length + 1))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var actual = TestAssetLoader.AllAssets.Values
            .Select(path => path.Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        actual.ShouldBe(expected);
    }
}
