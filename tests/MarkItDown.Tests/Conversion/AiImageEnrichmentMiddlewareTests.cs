using System.Reflection;
using MarkItDown.Conversion.Middleware;
using Shouldly;
using Xunit;

namespace MarkItDown.Tests.Conversion;

public sealed class AiImageEnrichmentMiddlewareTests
{
    [Fact]
    public void ReplaceImageBlock_RemovesLegacyImageComments()
    {
        const string oldPlaceholder = "![Image (page 34): See description below.](page-34.png)";
        const string newPlaceholder = "![Image (page 34): Detailed analytics dashboard view.](page-34.png)";
        const string legacyComment = """
<!-- Image description:
Image located on page 34.
-->
""";
        const string enrichedComment = """
<!-- Image description:
The image displays an analytics configuration form with labeled inputs and action buttons.
-->
""";

        var existingMarkdown = $"""
{oldPlaceholder}

{legacyComment}
""";

        var method = typeof(AiImageEnrichmentMiddleware).GetMethod(
            "ReplaceImageBlock",
            BindingFlags.Static | BindingFlags.NonPublic);

        method.ShouldNotBeNull();

        var updated = method.Invoke(null, new object?[]
        {
            existingMarkdown,
            oldPlaceholder,
            newPlaceholder,
            enrichedComment
        }) as string;

        updated.ShouldNotBeNull();
        updated.ShouldContain(newPlaceholder);
        updated.ShouldContain(enrichedComment);
        updated.ShouldNotContain("Image located on page 34.");
    }

    [Fact]
    public void ReplaceImageBlock_WithUpdatedPlaceholder_RemovesTrailingComments()
    {
        const string oldPlaceholder = "![Image (page 34): See description below.](page-34.png)";
        const string newPlaceholder = "![Image (page 34): Detailed analytics dashboard view.](page-34.png)";
        const string legacyComment = """
<!-- Image description:
Image located on page 34.
-->
""";
        const string enrichedComment = """
<!-- Image description:
The image displays an analytics configuration form with labeled inputs and action buttons.
-->
""";

        var existingMarkdown = $"""
{newPlaceholder}

{legacyComment}
""";

        var method = typeof(AiImageEnrichmentMiddleware).GetMethod(
            "ReplaceImageBlock",
            BindingFlags.Static | BindingFlags.NonPublic);

        method.ShouldNotBeNull();

        var updated = method.Invoke(null, new object?[]
        {
            existingMarkdown,
            oldPlaceholder,
            newPlaceholder,
            enrichedComment
        }) as string;

        updated.ShouldNotBeNull();
        updated.ShouldContain(newPlaceholder);
        updated.ShouldContain(enrichedComment);
        updated.ShouldNotContain("Image located on page 34.");
    }
}
