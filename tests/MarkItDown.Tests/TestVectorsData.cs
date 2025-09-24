using System;
using System.Collections.Generic;

namespace MarkItDown.Tests;

internal static class TestVectorsData
{
    public static IReadOnlyList<FileTestVector> General => general;

    private static readonly IReadOnlyList<FileTestVector> general = new List<FileTestVector>
    {
        new(
            FileName: "test.docx",
            MimeType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "AutoGen: Enabling Next-Gen LLM Applications via Multi-Agent Conversation",
                "d666f1f7-46cb-42bd-9a39-9a39cf2a509f",
                "314b0a30-5b04-470b-b9f7-eed2c2bec74a",
                "49e168b7-d2ae-407f-a055-2167576f39a1",
            },
            MustNotInclude: Array.Empty<string>(),
            SupportsStreamGuess: false
        ),
        new(
            FileName: "test_with_comment.docx",
            MimeType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "AutoGen: Enabling Next-Gen LLM Applications via Multi-Agent Conversation",
                "# Abstract",
            },
            MustNotInclude: Array.Empty<string>(),
            SupportsStreamGuess: false
        ),
        new(
            FileName: "test.xlsx",
            MimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "## Sheet1",
                "6ff4173b-42a5-4784-9b19-f49caff4d93d",
                "09060124-b5e7-4717-9d07-3c046eb",
                "affc7dad-52dc-4b98-9b5d-51e65d8a8ad0",
            },
            MustNotInclude: Array.Empty<string>(),
            SupportsStreamGuess: false
        ),
        new(
            FileName: "test.pptx",
            MimeType: "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "## Slide 1",
                "2cdda5c8-e50e-4db4-b5f0-9722a649f455",
                "AutoGen: Enabling Next-Gen LLM Applications via Multi-Agent Conversation",
            },
            MustNotInclude: Array.Empty<string>(),
            SupportsStreamGuess: false
        ),
        new(
            FileName: "test.pdf",
            MimeType: "application/pdf",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "Whilethereiscontemporaneous",
                "## Page Images",
            },
            MustNotInclude: Array.Empty<string>(),
            SupportsStreamGuess: false
        ),
        new(
            FileName: "test_blog.html",
            MimeType: "text/html",
            Charset: "utf-8",
            Url: "https://microsoft.github.io/autogen/blog/2023/04/21/LLM-tuning-math",
            MustInclude: new[]
            {
                "# Does Model and Inference Parameter Matter in LLM Applications? - A Case Study for MATH",
                "Large language models (LLMs) are powerful tools that can generate natural language texts",
            },
            MustNotInclude: Array.Empty<string>(),
            SupportsStreamGuess: false
        ),
        new(
            FileName: "test_wikipedia.html",
            MimeType: "text/html",
            Charset: "utf-8",
            Url: "https://en.wikipedia.org/wiki/Microsoft",
            MustInclude: new[]
            {
                "Microsoft was founded by [Bill Gates](/wiki/Bill_Gates)",
                "Microsoft entered the operating system (OS) business in 1980",
            },
            MustNotInclude: Array.Empty<string>(),
            SupportsStreamGuess: false
        ),
        new(
            FileName: "equations.docx",
            MimeType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "From Eq. 36.1.3",
            },
            MustNotInclude: Array.Empty<string>(),
            SupportsStreamGuess: false
        ),
        new(
            FileName: "test_serp.html",
            MimeType: "text/html",
            Charset: "utf-8",
            Url: "https://www.bing.com/search?q=microsoft+wikipedia",
            MustInclude: new[]
            {
                "https://en.wikipedia.org/wiki/Microsoft",
                "Microsoft Corporation is** an American multinational corporation and technology company headquartered**",
            },
            MustNotInclude: Array.Empty<string>()
        ),
        new(
            FileName: "test.epub",
            MimeType: "application/epub+zip",
            Charset: null,
            Url: null,
            MustInclude: Array.Empty<string>(),
            MustNotInclude: Array.Empty<string>(),
            SupportsStreamGuess: false,
            SupportsDataUri: false
        ),
        new(
            FileName: "test_mskanji.csv",
            MimeType: "text/csv",
            Charset: "cp932",
            Url: null,
            MustInclude: new[]
            {
                "| --- | --- | --- |",
            },
            MustNotInclude: Array.Empty<string>()
        ),
        new(
            FileName: "test.json",
            MimeType: "application/json",
            Charset: "ascii",
            Url: null,
            MustInclude: new[]
            {
                "5b64c88c-b3c3-4510-bcb8-da0b200602d8",
                "9700dc99-6685-40b4-9a3a-5e406dcb37f3",
            },
            MustNotInclude: Array.Empty<string>()
        ),
        new(
            FileName: "test_rss.xml",
            MimeType: "text/xml",
            Charset: "utf-8",
            Url: null,
            MustInclude: new[]
            {
                "# The Official Microsoft Blog",
                "Ignite 2024: Why nearly 70% of the Fortune 500 now use Microsoft 365 Copilot",
            },
            MustNotInclude: new[]
            {
                "<rss",
            }
        ),
        new(
            FileName: "test_notebook.ipynb",
            MimeType: "application/json",
            Charset: "ascii",
            Url: null,
            MustInclude: new[]
            {
                "# Test Notebook",
                "print(\\\"markitdown\\\")",
            },
            MustNotInclude: Array.Empty<string>()
        ),
        new(
            FileName: "test_files.zip",
            MimeType: "application/zip",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "# Content from",
                "## File: test_blog.html",
            },
            MustNotInclude: Array.Empty<string>()
        ),
        new(
            FileName: "test.mp3",
            MimeType: "audio/mpeg",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "*No audio metadata available.*",
            },
            MustNotInclude: Array.Empty<string>(),
            SupportsStreamGuess: false
        ),
        new(
            FileName: "test.m4a",
            MimeType: "audio/mp4",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "*No audio metadata available.*",
            },
            MustNotInclude: Array.Empty<string>(),
            SupportsStreamGuess: false
        ),
        new(
            FileName: "test.wav",
            MimeType: "audio/x-wav",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "*No audio metadata available.*",
            },
            MustNotInclude: Array.Empty<string>(),
            SupportsStreamGuess: false
        ),
        new(
            FileName: "test_llm.jpg",
            MimeType: "image/jpeg",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "*No image metadata available.*",
            },
            MustNotInclude: Array.Empty<string>()
        ),
        new(
            FileName: "test.jpg",
            MimeType: "image/jpeg",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "*No image metadata available.*",
            },
            MustNotInclude: Array.Empty<string>(),
            SupportsStreamGuess: false
        ),
    };
}
