using MarkItDown.Converters;

namespace MarkItDown.Tests;

public class UpstreamWebParityTests
{
    [Theory]
    [InlineData("content", "text", "Use &lt;tag&gt; literally", "Use <tag> literally")]
    [InlineData("summary", "text", "Use &lt;tag&gt; literally", "Use <tag> literally")]
    [InlineData("content", "html", "&lt;p&gt;Read &lt;strong&gt;carefully&lt;/strong&gt;&lt;/p&gt;", "Read **carefully**")]
    [InlineData("summary", "html", "&lt;p&gt;Read &lt;strong&gt;carefully&lt;/strong&gt;&lt;/p&gt;", "Read **carefully**")]
    [InlineData("content", "xhtml", "<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>Read <strong>carefully</strong></p></div>", "Read **carefully**")]
    [InlineData("summary", "xhtml", "<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>Read <strong>carefully</strong></p></div>", "Read **carefully**")]
    [InlineData("content", "xhtml", "<x:div xmlns:x=\"http://www.w3.org/1999/xhtml\"><x:p>Read <x:strong>carefully</x:strong></x:p></x:div>", "Read **carefully**")]
    public async Task AtomTextConstructs_PreserveContentType(string element, string type, string content, string expected)
    {
        var xml = $"<feed xmlns=\"http://www.w3.org/2005/Atom\"><title>Feed</title><entry><title>Entry</title><{element} type=\"{type}\">{content}</{element}></entry></feed>";
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, xml);
            await using var stream = File.OpenRead(path);
            var result = await new RssFeedConverter().ConvertAsync(stream, new StreamInfo(extension: ".atom"));
            Assert.Contains(expected, result.Markdown);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("<s>Removed</s>")]
    [InlineData("<del>Removed</del>")]
    [InlineData("<strike>Removed</strike>")]
    [InlineData("<span style=\"text-decoration: line-through\">Removed</span>")]
    [InlineData("<span style=\"text-decoration-line: underline line-through\">Removed</span>")]
    public async Task HtmlStrikethrough_PreservesDeletedText(string content)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, $"<html><body><p>{content}</p></body></html>");
            await using var stream = File.OpenRead(path);
            var result = await new HtmlConverter().ConvertAsync(stream, new StreamInfo(extension: ".html"));
            Assert.Contains("~~Removed~~", result.Markdown);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("<a href=\"https://example.test\" style=\"text-decoration:line-through\">Removed</a>", "[~~Removed~~](https://example.test)")]
    [InlineData("<h2 style=\"text-decoration:line-through\">Removed</h2>", "## ~~Removed~~")]
    [InlineData("<p style=\"text-decoration:line-through\">Removed</p><p>Next</p>", "~~Removed~~\n\nNext")]
    [InlineData("<span style=\"text-decoration:line-through;text-decoration:none\">Current</span>", "Current")]
    [InlineData("<span style=\"text-decoration:line-through!important;text-decoration:none\">Removed</span>", "~~Removed~~")]
    public async Task HtmlStrikethrough_PreservesElementSemanticsAndCssOrder(string content, string expected)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, $"<html><body>{content}</body></html>");
            await using var stream = File.OpenRead(path);
            var result = await new HtmlConverter().ConvertAsync(stream, new StreamInfo(extension: ".html"));
            Assert.Equal(expected, result.Markdown);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
