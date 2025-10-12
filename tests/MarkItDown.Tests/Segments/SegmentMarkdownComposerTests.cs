using System.Collections.Generic;
using MarkItDown;
using Shouldly;
using Xunit;

namespace MarkItDown.Tests.Segments;

public sealed class SegmentMarkdownComposerTests
{
    [Fact]
    public void Compose_SkipsCommentBlocks_WhenDerivingTitle()
    {
        var segments = new List<DocumentSegment>
        {
            new DocumentSegment("""
<!-- Image description:
Comment content that should not drive the title.
-->
![Placeholder](image.png)
""", SegmentType.Image),
            new DocumentSegment("# Actual Title\nBody", SegmentType.Page)
        };

        var streamInfo = new StreamInfo(fileName: "sample.docx");
        var result = SegmentMarkdownComposer.Compose(segments, new ConversionArtifacts(), streamInfo, SegmentOptions.Default);

        result.Title.ShouldBe("Actual Title");
    }
}
