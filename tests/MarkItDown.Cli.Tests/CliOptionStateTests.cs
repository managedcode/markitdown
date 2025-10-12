using System;
using Shouldly;
using Xunit;

namespace MarkItDown.Cli.Tests;

public class CliOptionStateTests
{
    [Fact]
    public void BuildOptions_DefaultsToEmptyProviders()
    {
        var state = new CliOptionState();
        var options = state.BuildOptions();

        options.AzureIntelligence.ShouldBeNull();
        options.GoogleIntelligence.ShouldBeNull();
        options.AwsIntelligence.ShouldBeNull();
    }

    [Fact]
    public void BuildOptions_UsesConfiguredSegments()
    {
        var state = new CliOptionState
        {
            SegmentOptions = SegmentOptions.Default with
            {
                IncludeSegmentMetadataInMarkdown = true,
                Audio = SegmentOptions.Default.Audio with
                {
                    SegmentDuration = TimeSpan.FromMinutes(5)
                }
            }
        };

        var options = state.BuildOptions();
        options.Segments.IncludeSegmentMetadataInMarkdown.ShouldBeTrue();
        options.Segments.Audio.SegmentDuration.ShouldBe(TimeSpan.FromMinutes(5));
    }
}
