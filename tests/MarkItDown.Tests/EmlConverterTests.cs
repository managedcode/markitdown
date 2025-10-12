using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MarkItDown;
using MarkItDown.Converters;
using Shouldly;

namespace MarkItDown.Tests;

public class EmlConverterTests
{
    private const string SampleEmail = @"Return-Path: <sender@example.com>
Received: from mail.example.com (mail.example.com [192.168.1.1])
	by recipient.example.com with SMTP; Mon, 15 Jan 2024 10:30:00 +0000
Date: Mon, 15 Jan 2024 10:30:00 +0000
From: John Doe <sender@example.com>
To: Jane Smith <recipient@example.com>
Cc: Team <team@example.com>
Subject: Important Project Update
Message-ID: <123456789@example.com>
MIME-Version: 1.0
Content-Type: text/plain; charset=UTF-8

Hello Jane,

I wanted to update you on the current project status:

1. Phase 1 is complete
2. Phase 2 is in progress
3. Phase 3 starts next week

Key points:
- Budget is on track
- Timeline looks good
- Team morale is high

Please let me know if you have any questions.

Best regards,
John";

    [Fact]
    public async Task ConvertAsync_ValidEmlContent_ReturnsCorrectMarkdown()
    {
        // Arrange
        var converter = new EmlConverter();
        var bytes = Encoding.UTF8.GetBytes(SampleEmail);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(mimeType: "message/rfc822", extension: ".eml");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        result.ShouldNotBeNull();
        result.Markdown.ShouldNotBeNullOrWhiteSpace();
        result.Title.ShouldBe("Important Project Update");
        
        // Check essential email headers are present
        result.Markdown.ShouldContain("**Subject:** Important Project Update");
        result.Markdown.ShouldContain("**From:** John Doe <sender@example.com>");
        result.Markdown.ShouldContain("**To:** Jane Smith <recipient@example.com>");
        result.Markdown.ShouldContain("**CC:** Team <team@example.com>");
        result.Markdown.ShouldContain("**Date:** 2024-01-15 10:30:00 +00:00");
        
        // Check message content is included
        result.Markdown.ShouldContain("Hello Jane");
        result.Markdown.ShouldContain("Phase 1 is complete");
        result.Markdown.ShouldContain("Best regards");
        result.Markdown.ShouldContain("John");
    }

    [Fact]
    public async Task ConvertAsync_EmailWithoutSubject_UsesFromAsFallbackTitle()
    {
        // Arrange
        var emailWithoutSubject = @"Date: Mon, 15 Jan 2024 10:30:00 +0000
From: John Doe <sender@example.com>
To: Jane Smith <recipient@example.com>
MIME-Version: 1.0
Content-Type: text/plain; charset=UTF-8

This is a simple message without a subject.";

        var converter = new EmlConverter();
        var bytes = Encoding.UTF8.GetBytes(emailWithoutSubject);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(mimeType: "message/rfc822");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        result.ShouldNotBeNull();
        result.Title.ShouldBe("Email from John Doe <sender@example.com>");
    }

    [Fact]
    public async Task ConvertAsync_EmailWithHtmlContent_ConvertsHtmlToMarkdown()
    {
        // Arrange
        var htmlEmail = @"Date: Mon, 15 Jan 2024 10:30:00 +0000
From: sender@example.com
To: recipient@example.com
Subject: HTML Test
MIME-Version: 1.0
Content-Type: text/html; charset=UTF-8

<html>
<body>
<h1>Welcome</h1>
<p>This is <strong>bold</strong> text and <em>italic</em> text.</p>
<ul>
<li>Item 1</li>
<li>Item 2</li>
</ul>
</body>
</html>";

        var converter = new EmlConverter();
        var bytes = Encoding.UTF8.GetBytes(htmlEmail);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(mimeType: "message/rfc822");

        // Act
        var result = await converter.ConvertAsync(stream, streamInfo);

        // Assert
        result.ShouldNotBeNull();
        result.Markdown.ShouldNotBeNullOrWhiteSpace();
        result.Title.ShouldBe("HTML Test");
        
        // Check that HTML was converted to Markdown
        result.Markdown.ShouldContain("# Welcome");
        result.Markdown.ShouldContain("**bold**");
        result.Markdown.ShouldContain("*italic*");
    }

    [Fact]
    public async Task MarkItDown_ConvertAsync_EmlFile_WorksEndToEnd()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDownClient();
        var bytes = Encoding.UTF8.GetBytes(SampleEmail);
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(mimeType: "message/rfc822", extension: ".eml");

        // Act
        var result = await markItDown.ConvertAsync(stream, streamInfo);

        // Assert
        result.ShouldNotBeNull();
        result.Markdown.ShouldNotBeNullOrWhiteSpace();
        result.Title.ShouldBe("Important Project Update");
        result.Markdown.ShouldContain("**Subject:** Important Project Update");
        result.Markdown.ShouldContain("Hello Jane");
    }
}