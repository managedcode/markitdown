using System;
using System.IO;
using System.Threading.Tasks;
using MarkItDown;
using Shouldly;
using Xunit;

namespace MarkItDown.Cli.Tests;

public class ConversionServiceTests
{
    [Fact]
    public async Task ConvertFilesAsync_WritesMarkdownOutput()
    {
        var tempRoot = Path.Combine(Environment.CurrentDirectory, ".markitdown-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try 
        {
            var inputFile = Path.Combine(tempRoot, "sample.txt");
            await File.WriteAllTextAsync(inputFile, "Hello from MarkItDown");
            var outputDir = Path.Combine(tempRoot, "output");

            var service = new ConversionService();
            var options = new MarkItDownOptions();
            var summary = await service.ConvertFilesAsync(new[] { inputFile }, outputDir, options);

            summary.SuccessCount.ShouldBe(1);
            summary.FailureCount.ShouldBe(0);
            summary.Results.Count.ShouldBe(1);
            var outputPath = summary.Results[0].Output;
            outputPath.ShouldNotBeNull();
            File.Exists(outputPath!).ShouldBeTrue();
            var markdown = await File.ReadAllTextAsync(outputPath!);
            markdown.ShouldContain("Hello from MarkItDown");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
