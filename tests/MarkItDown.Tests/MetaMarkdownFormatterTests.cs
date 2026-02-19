using System;
using System.Collections.Generic;
using System.Text.Json;
using MarkItDown;
using Xunit;

namespace MarkItDown.Tests;

public class MetaMarkdownFormatterTests
{
    private static readonly string[] value = new[] { "Chart", "Summary" };
    private static readonly string[] valueArray = new[] { "Column A", "Column B" };
    private static readonly string[] valueArray0 = new[] { "Value A1", "Value B1" };

    [Fact]
    public void BuildImageComment_ProducesStructuredMetaMdBlock()
    {
        // Arrange
        var image = new ImageArtifact(new byte[] { 1, 2, 3 }, "image/png", pageNumber: 3, source: "page-3.png")
        {
            RawText = "Example heading"
        };

        image.Metadata[MetadataKeys.DetailedDescription] = "A concise description of the screenshot.";
        image.Metadata[MetadataKeys.LayoutRegions] = JsonSerializer.Serialize(new[]
        {
            new { id = "main", position = "Center", elements = value }
        });
        image.Metadata[MetadataKeys.StructuredTables] = JsonSerializer.Serialize(new[]
        {
            new
            {
                title = "Sample Table",
                headers = valueArray,
                rows = new[] { valueArray0 },
                notes = "Illustrative data."
            }
        });

        // Act
        var comment = MetaMarkdownFormatter.BuildImageComment(image);

        // Assert
        Assert.NotNull(comment);
        Assert.StartsWith("<!-- Image description:", comment, StringComparison.Ordinal);
        Assert.EndsWith("-->", comment, StringComparison.Ordinal);
        Assert.Contains("Visible text:", comment, StringComparison.Ordinal);
        Assert.Contains("Table data:", comment, StringComparison.Ordinal);
        Assert.DoesNotContain("### Metadata", comment!, StringComparison.Ordinal);
        Assert.DoesNotContain("ai.", comment!, StringComparison.Ordinal);
    }
}
