using System.Collections.Generic;
using MarkItDown.Core;

namespace MarkItDown.Tests;

public record FileTestVector(
    string Filename,
    string? MimeType,
    string? Charset,
    string? Url,
    string[] MustInclude,
    string[] MustNotInclude
);

public static class TestVectors
{
    public static readonly FileTestVector[] GeneralTestVectors = 
    {
        new FileTestVector(
            Filename: "test.docx",
            MimeType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "314b0a30-5b04-470b-b9f7-eed2c2bec74a",
                "49e168b7-d2ae-407f-a055-2167576f39a1",
                "## d666f1f7-46cb-42bd-9a39-9a39cf2a509f",
                "# Abstract",
                "# Introduction",
                "AutoGen: Enabling Next-Gen LLM Applications via Multi-Agent Conversation",
                "data:image/png;base64..."
            },
            MustNotInclude: new[]
            {
                "data:image/png;base64,iVBORw0KGgoAAAANSU"
            }
        ),
        
        new FileTestVector(
            Filename: "test.xlsx",
            MimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "## 09060124-b5e7-4717-9d07-3c046eb",
                "6ff4173b-42a5-4784-9b19-f49caff4d93d",
                "affc7dad-52dc-4b98-9b5d-51e65d8a8ad0"
            },
            MustNotInclude: new string[0]
        ),
        
        new FileTestVector(
            Filename: "test.xls",
            MimeType: "application/vnd.ms-excel",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "## 09060124-b5e7-4717-9d07-3c046eb",
                "6ff4173b-42a5-4784-9b19-f49caff4d93d",
                "affc7dad-52dc-4b98-9b5d-51e65d8a8ad0"
            },
            MustNotInclude: new string[0]
        ),
        
        new FileTestVector(
            Filename: "test.pptx",
            MimeType: "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "2cdda5c8-e50e-4db4-b5f0-9722a649f455",
                "04191ea8-5c73-4215-a1d3-1cfb43aaaf12",
                "44bf7d06-5e7a-4a40-a2e1-a2e42ef28c8a",
                "1b92870d-e3b5-4e65-8153-919f4ff45592",
                "AutoGen: Enabling Next-Gen LLM Applications via Multi-Agent Conversation",
                "a3f6004b-6f4f-4ea8-bee3-3741f4dc385f",
                "2003",
                "![This phrase of the caption is Human-written.](Picture4.jpg)"
            },
            MustNotInclude: new[]
            {
                "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQE"
            }
        ),
        
        new FileTestVector(
            Filename: "test_outlook_msg.msg",
            MimeType: "application/vnd.ms-outlook",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "# Email Message",
                "**From:** test.sender@example.com",
                "**To:** test.recipient@example.com",
                "**Subject:** Test Email Message",
                "## Content",
                "This is the body of the test email message"
            },
            MustNotInclude: new string[0]
        ),
        
        new FileTestVector(
            Filename: "test.pdf",
            MimeType: "application/pdf",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "While there is contemporaneous exploration of multi-agent approaches"
            },
            MustNotInclude: new string[0]
        ),
        
        new FileTestVector(
            Filename: "test_blog.html",
            MimeType: "text/html",
            Charset: "utf-8",
            Url: "https://microsoft.github.io/autogen/blog/2023/04/21/LLM-tuning-math",
            MustInclude: new[]
            {
                "Large language models (LLMs) are powerful tools that can generate natural language texts for various applications, such as chatbots, summarization, translation, and more. GPT-4 is currently the state of the art LLM in the world. Is model selection irrelevant? What about inference parameters?",
                "an example where high cost can easily prevent a generic complex"
            },
            MustNotInclude: new string[0]
        ),
        
        new FileTestVector(
            Filename: "test_wikipedia.html",
            MimeType: "text/html",
            Charset: "utf-8",
            Url: "https://en.wikipedia.org/wiki/Microsoft",
            MustInclude: new[]
            {
                "Microsoft entered the operating system (OS) business in 1980 with its own version of [Unix]",
                "Microsoft was founded by [Bill Gates](/wiki/Bill_Gates \"Bill Gates\")"
            },
            MustNotInclude: new[]
            {
                "You are encouraged to create an account and log in",
                "154 languages",
                "move to sidebar"
            }
        ),
        
        new FileTestVector(
            Filename: "test_serp.html",
            MimeType: "text/html",
            Charset: "utf-8",
            Url: "https://www.bing.com/search?q=microsoft+wikipedia",
            MustInclude: new[]
            {
                "](https://en.wikipedia.org/wiki/Microsoft",
                "Microsoft Corporation is **an American multinational corporation and technology company headquartered** in Redmond",
                "1995–2007: Foray into the Web, Windows 95, Windows XP, and Xbox"
            },
            MustNotInclude: new[]
            {
                "https://www.bing.com/ck/a?!&&p=",
                "data:image/svg+xml,%3Csvg%20width%3D"
            }
        ),
        
        new FileTestVector(
            Filename: "test_mskanji.csv",
            MimeType: "text/csv",
            Charset: "cp932",
            Url: null,
            MustInclude: new[]
            {
                "| 名前 | 年齢 | 住所 |",
                "| --- | --- | --- |",
                "| 佐藤太郎 | 30 | 東京 |",
                "| 三木英子 | 25 | 大阪 |",
                "| 髙橋淳 | 35 | 名古屋 |"
            },
            MustNotInclude: new string[0]
        ),
        
        new FileTestVector(
            Filename: "test.json",
            MimeType: "application/json",
            Charset: "ascii",
            Url: null,
            MustInclude: new[]
            {
                "5b64c88c-b3c3-4510-bcb8-da0b200602d8",
                "9700dc99-6685-40b4-9a3a-5e406dcb37f3"
            },
            MustNotInclude: new string[0]
        ),
        
        new FileTestVector(
            Filename: "test_rss.xml",
            MimeType: "text/xml",
            Charset: "utf-8",
            Url: null,
            MustInclude: new[]
            {
                "# The Official Microsoft Blog",
                "## Ignite 2024: Why nearly 70% of the Fortune 500 now use Microsoft 365 Copilot",
                "In the case of AI, it is absolutely true that the industry is moving incredibly fast"
            },
            MustNotInclude: new[]
            {
                "<rss",
                "<feed"
            }
        ),
        
        new FileTestVector(
            Filename: "test_notebook.ipynb",
            MimeType: "application/json",
            Charset: "ascii",
            Url: null,
            MustInclude: new[]
            {
                "# Test Notebook",
                "```python",
                "print(\"markitdown\")",
                "```",
                "## Code Cell Below"
            },
            MustNotInclude: new[]
            {
                "nbformat",
                "nbformat_minor"
            }
        ),
        
        new FileTestVector(
            Filename: "test.jpg",
            MimeType: "image/jpeg",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "AutoGen: Enabling Next-Gen LLM Applications via Multi-Agent Conversation",
                "AutoGen enables diverse LLM-based applications"
            },
            MustNotInclude: new string[0]
        ),
        
        new FileTestVector(
            Filename: "test_llm.jpg",
            MimeType: "image/jpeg",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "The image appears to be"
            },
            MustNotInclude: new string[0]
        ),
        
        new FileTestVector(
            Filename: "test.mp3",
            MimeType: "audio/mpeg",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "f67a499e-a7d0-4ca3-a49b-358bd934ae3e",
                "Artist Name Test String",
                "Album Name Test String"
            },
            MustNotInclude: new string[0]
        ),
        
        new FileTestVector(
            Filename: "test.wav",
            MimeType: "audio/wav",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "This is a test audio file for MarkItDown"
            },
            MustNotInclude: new string[0]
        ),
        
        new FileTestVector(
            Filename: "test.m4a",
            MimeType: "audio/m4a",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "This is a test audio file for MarkItDown"
            },
            MustNotInclude: new string[0]
        ),
        
        new FileTestVector(
            Filename: "test.epub",
            MimeType: "application/epub+zip",
            Charset: null,
            Url: null,
            MustInclude: new[]
            {
                "# The Great Gatsby",
                "## Chapter 1",
                "In my younger and more vulnerable years"
            },
            MustNotInclude: new string[0]
        )
    };

    public static readonly Dictionary<string, string> DataUriTestVectors = new()
    {
        ["simple_text"] = "data:text/plain;base64,SGVsbG8gV29ybGQ=",
        ["html_content"] = "data:text/html;base64,PGgxPkhlbGxvPC9oMT4=",
        ["json_content"] = "data:application/json;base64,eyJ0ZXN0IjoidmFsdWUifQ=="
    };
}