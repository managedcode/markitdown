# MarkItDown

[![.NET](https://img.shields.io/badge/.NET-9.0+-blue)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![NuGet](https://img.shields.io/nuget/v/ManagedCode.MarkItDown.svg)](https://www.nuget.org/packages/ManagedCode.MarkItDown)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A modern C#/.NET library for converting a wide range of document formats (HTML, PDF, DOCX, XLSX, EPUB, archives, URLs, etc.) into high-quality Markdown suitable for Large Language Models (LLMs), search indexing, and text analytics. The project mirrors the original Microsoft Python implementation while embracing .NET idioms, async APIs, and new integrations.

## Table of Contents

- [Features](#features)
- [Format Support](#-format-support)
- [Quick Start](#-quick-start)
- [Usage](#-usage)
- [Architecture](#-architecture)
- [Development & Contributing](#-development--contributing)
- [Roadmap](#-roadmap)
- [Performance](#-performance)
- [Configuration](#-configuration)
- [License](#-license)
- [Acknowledgments](#-acknowledgments)
- [Support](#-support)

## Features

✨ **Modern .NET** - Targets .NET 9.0 with up-to-date language features  
📦 **NuGet Package** - Drop-in dependency for libraries and automation pipelines  
🔄 **Async/Await** - Fully asynchronous pipeline for responsive apps  
🧠 **LLM-Optimized** - Markdown tailored for AI ingestion and summarisation  
🔧 **Extensible** - Register custom converters or plug additional caption/transcription services  
🧭 **Smart Detection** - Automatic MIME, charset, and file-type guessing (including data/file URIs)  
⚡ **High Performance** - Stream-friendly, minimal allocations, zero temp files

## 📋 Format Support

| Format | Extension | Status | Description |
|--------|-----------|---------|-------------|
| **HTML** | `.html`, `.htm` | ✅ Supported | Full HTML to Markdown conversion |
| **Plain Text** | `.txt`, `.md` | ✅ Supported | Direct text processing |
| **PDF** | `.pdf` | ✅ Supported | Adobe PDF documents with text extraction |
| **Word** | `.docx` | ✅ Supported | Microsoft Word documents with formatting |
| **Excel** | `.xlsx` | ✅ Supported | Microsoft Excel spreadsheets as tables |
| **PowerPoint** | `.pptx` | ✅ Supported | Microsoft PowerPoint presentations |
| **Images** | `.jpg`, `.png`, `.gif`, `.bmp`, `.tiff`, `.webp` | ✅ Supported | Exif metadata extraction + optional captions |
| **Audio** | `.wav`, `.mp3`, `.m4a`, `.mp4` | ✅ Supported | Metadata extraction + optional transcription |
| **CSV** | `.csv` | ✅ Supported | Comma-separated values as Markdown tables |
| **JSON** | `.json`, `.jsonl`, `.ndjson` | ✅ Supported | Structured JSON data with formatting |
| **XML** | `.xml`, `.xsd`, `.xsl`, `.rss`, `.atom` | ✅ Supported | XML documents with structure preservation |
| **EPUB** | `.epub` | ✅ Supported | E-book files with metadata and content |
| **ZIP** | `.zip` | ✅ Supported | Archive processing with recursive file conversion |
| **Jupyter Notebook** | `.ipynb` | ✅ Supported | Python notebooks with code and markdown cells |
| **RSS/Atom Feeds** | `.rss`, `.atom`, `.xml` | ✅ Supported | Web feeds with structured content and metadata |
| **YouTube URLs** | YouTube links | ✅ Supported | Video metadata extraction and link formatting |
| **Wikipedia Pages** | wikipedia.org | ✅ Supported | Article-only extraction with clean Markdown |
| **Bing SERPs** | bing.com/search | ✅ Supported | Organic result summarisation |

### HTML Conversion Features (AngleSharp powered)
- Headers (H1-H6) → Markdown headers
- Bold/Strong text → **bold**
- Italic/Emphasis text → *italic*
- Links → [text](url)
- Images → ![alt](src)
- Lists (ordered/unordered)
- Tables with header detection and Markdown table output
- Code blocks and inline code
- Blockquotes, sections, semantic containers

### PDF Conversion Features
- Text extraction with page separation
- Header detection based on formatting
- List item recognition
- Title extraction from document content

### Office Documents (DOCX/XLSX/PPTX)
- **Word (.docx)**: Headers, paragraphs, tables, bold/italic formatting
- **Excel (.xlsx)**: Spreadsheet data as Markdown tables with sheet organization
- **PowerPoint (.pptx)**: Slide-by-slide content with title recognition

### CSV Conversion Features
- Automatic table formatting with headers
- Proper escaping of special characters
- Support for various CSV dialects
- Handles quoted fields and embedded commas

### JSON Conversion Features
- **Structured Format**: Converts JSON objects to readable Markdown with proper hierarchy
- **JSON Lines Support**: Processes `.jsonl` and `.ndjson` files line by line
- **Data Type Preservation**: Maintains JSON data types (strings, numbers, booleans, null)
- **Nested Objects**: Handles complex nested structures with proper indentation

### XML Conversion Features
- **Structure Preservation**: Maintains XML hierarchy as Markdown headings
- **Attributes Handling**: Converts XML attributes to Markdown lists
- **Multiple Formats**: Supports XML, XSD, XSL, RSS, and Atom feeds
- **CDATA Support**: Properly handles CDATA sections as code blocks

### EPUB Conversion Features
- **Metadata Extraction**: Extracts title, author, publisher, and other Dublin Core metadata
- **Content Order**: Processes content files in proper reading order using spine information
- **HTML Processing**: Converts XHTML content using the HTML converter
- **Table of Contents**: Maintains document structure from the original EPUB

### ZIP Archive Features
- **Recursive Processing**: Extracts and converts all supported files within archives
- **Structure Preservation**: Maintains original file paths and organization
- **Multi-Format Support**: Processes different file types within the same archive
- **Error Handling**: Continues processing even if individual files fail
- **Size Limits**: Protects against memory issues with large files

### Jupyter Notebook Conversion Features
- **Cell Type Support**: Processes markdown, code, and raw cells appropriately
- **Metadata Extraction**: Extracts notebook title, kernel information, and language details
- **Code Output Handling**: Captures and formats execution results, streams, and errors
- **Syntax Highlighting**: Preserves language information for proper code block formatting

### RSS/Atom Feed Conversion Features  
- **Multi-Format Support**: Handles RSS 2.0, RSS 1.0 (RDF), and Atom 1.0 feeds
- **Feed Metadata**: Extracts title, description, last update date, and author information
- **Article Processing**: Converts feed items with proper title linking and content formatting
- **Date Formatting**: Normalizes publication dates across different feed formats

### YouTube URL Conversion Features
- **URL Recognition**: Supports standard and shortened YouTube URLs (youtube.com, youtu.be)
- **Metadata Extraction**: Extracts video ID and URL parameters with descriptions
- **Embed Integration**: Provides thumbnail images and multiple access methods
- **Parameter Parsing**: Decodes common YouTube URL parameters (playlist, timestamps, etc.)

### Image Conversion Features
- Support for JPEG, PNG, GIF, BMP, TIFF, WebP
- Exif metadata extraction via `exiftool` (optional)
- Optional multimodal image captioning hook (LLM integration ready)
- Graceful fallback when metadata/captioning unavailable

### Audio Conversion Features
- Handles WAV/MP3/M4A/MP4 containers
- Extracts key metadata (artist, album, duration, channels, etc.)
- Optional transcription delegate for speech-to-text results
- Markdown summary highlighting metadata and transcript

## 🚀 Quick Start

### Installation

Install via NuGet Package Manager:

```bash
# Package Manager Console
Install-Package ManagedCode.MarkItDown

# .NET CLI
dotnet add package ManagedCode.MarkItDown

# PackageReference (add to your .csproj)
<PackageReference Include="ManagedCode.MarkItDown" Version="1.0.0" />
```

### Prerequisites
- .NET 9.0 SDK or later
- Compatible with .NET 9 apps and libraries

### Optional Dependencies for Advanced Features
- **PDF Support**: Provided via PdfPig (bundled)
- **Office Documents**: Provided via DocumentFormat.OpenXml (bundled)
- **Image metadata**: Install [ExifTool](https://exiftool.org/) for richer output (`brew install exiftool`, `choco install exiftool`)
- **Image captions**: Supply an `ImageCaptioner` delegate (e.g., calls to an LLM or vision service)
- **Audio transcription**: Supply an `AudioTranscriber` delegate (e.g., Azure Cognitive Services, OpenAI Whisper)

> **Note**: External tools are optional—MarkItDown degrades gracefully when they are absent.

## 💻 Usage

### Convert a local file

```csharp
using MarkItDown;

// Convert a DOCX file and print the Markdown
var markItDown = new MarkItDown();
DocumentConverterResult result = await markItDown.ConvertAsync("report.docx");
Console.WriteLine(result.Markdown);
```

### Convert a stream with metadata overrides

```csharp
using System.IO;
using System.Text;
using MarkItDown;

using var stream = File.OpenRead("invoice.html");
var streamInfo = new StreamInfo(
    mimeType: "text/html",
    extension: ".html",
    charset: Encoding.UTF8,
    fileName: "invoice.html");

var markItDown = new MarkItDown();
var result = await markItDown.ConvertAsync(stream, streamInfo);
Console.WriteLine(result.Title);
```

### Convert content from HTTP/HTTPS

```csharp
using MarkItDown;
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(static builder => builder.AddConsole());
using var httpClient = new HttpClient();

var markItDown = new MarkItDown(
    logger: loggerFactory.CreateLogger<MarkItDown>(),
    httpClient: httpClient);

DocumentConverterResult urlResult = await markItDown.ConvertFromUrlAsync("https://contoso.example/blog");
Console.WriteLine(urlResult.Title);
```

### Customise the pipeline with options

```csharp
using Azure;
using MarkItDown;

var options = new MarkItDownOptions
{
    // Plug in your own services (Azure AI, OpenAI, etc.)
    ImageCaptioner = async (bytes, info, token) =>
        await myCaptionService.DescribeAsync(bytes, info, token),
    AudioTranscriber = async (bytes, info, token) =>
        await speechClient.TranscribeAsync(bytes, info, token),
    DocumentIntelligence = new DocumentIntelligenceOptions
    {
        Endpoint = "https://<your-resource>.cognitiveservices.azure.com/",
        Credential = new AzureKeyCredential("<document-intelligence-key>")
    }
};

var markItDown = new MarkItDown(options);
```

### Custom converters

Create your own format converters by implementing `IDocumentConverter`:

```csharp
using System.IO;
using MarkItDown;

public sealed class MyCustomConverter : IDocumentConverter
{
    public int Priority => ConverterPriority.SpecificFileFormat;

    public bool AcceptsInput(StreamInfo streamInfo) =>
        string.Equals(streamInfo.Extension, ".mycustom", StringComparison.OrdinalIgnoreCase);

    public Task<DocumentConverterResult> ConvertAsync(
        Stream stream,
        StreamInfo streamInfo,
        CancellationToken cancellationToken = default)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, leaveOpen: true);
        var markdown = "# Converted from custom format\n\n" + reader.ReadToEnd();
        return Task.FromResult(new DocumentConverterResult(markdown, "Custom document"));
    }
}

var markItDown = new MarkItDown();
markItDown.RegisterConverter(new MyCustomConverter());
```

## 🏗️ Architecture

### Core Components

- **`MarkItDown`** - Main entry point for conversions
- **`IDocumentConverter`** - Interface for format-specific converters
- **`DocumentConverterResult`** - Contains the converted Markdown and optional metadata
- **`StreamInfo`** - Metadata about the input stream (MIME type, extension, charset, etc.)
- **`ConverterRegistration`** - Associates converters with priority for selection

### Built-in Converters

- **`PlainTextConverter`** - Handles text, JSON, NDJSON, Markdown, etc.
- **`HtmlConverter`** - Converts HTML to Markdown using AngleSharp
- **`PdfConverter`** - PdfPig-based extraction with Markdown heuristics
- **`Docx/Xlsx/Pptx` Converters** - Office Open XML processing
- **`ImageConverter`** - Exif metadata + optional captions
- **`AudioConverter`** - Metadata + optional transcription
- **`WikipediaConverter`** - Article-only extraction from Wikipedia
- **`BingSerpConverter`** - Summaries for Bing search result pages
- **`YouTubeUrlConverter`** - Video metadata markdown
- **`ZipConverter`** - Recursive archive handling
- **`RssFeedConverter`**, **`JsonConverter`**, **`CsvConverter`**, **`XmlConverter`**, **`JupyterNotebookConverter`**, **`EpubConverter`**

### Converter Priority & Detection

- Priority-based dispatch (lower values processed first)
- Automatic stream sniffing via `StreamInfoGuesser`
- Manual overrides via `MarkItDownOptions` or `StreamInfo`

## 🔄 Development & Contributing

### Building from Source

```bash
# Clone the repository
git clone https://github.com/managedcode/markitdown.git
cd markitdown

# Build the solution
dotnet build

# Run tests
dotnet test

# Create NuGet package
dotnet pack --configuration Release
```

### Tests & Coverage

```bash
dotnet test --collect:"XPlat Code Coverage"
```

The command emits standard test results plus a Cobertura coverage report at
`tests/MarkItDown.Tests/TestResults/<guid>/coverage.cobertura.xml`. Tools such as
[ReportGenerator](https://github.com/danielpalme/ReportGenerator) can turn this into
HTML or Markdown dashboards.

### Project Structure

```
├── src/
│   ├── MarkItDown/                # Core library
│   │   ├── Converters/            # Format-specific converters (HTML, PDF, audio, etc.)
│   │   ├── MarkItDown.cs          # Main conversion engine
│   │   ├── StreamInfoGuesser.cs   # MIME/charset/extension detection helpers
│   │   ├── MarkItDownOptions.cs   # Runtime configuration flags
│   │   └── ...                    # Shared utilities (UriUtilities, MimeMapping, etc.)
│   └── MarkItDown.Cli/            # CLI host (under active development)
├── tests/
│   └── MarkItDown.Tests/          # xUnit + Shouldly tests, Python parity vectors (WIP)
├── Directory.Build.props          # Shared build + packaging settings
└── README.md                      # This document
```

### Contributing Guidelines

1. **Fork** the repository.
2. **Create** a feature branch (`git checkout -b feature/my-feature`).
3. **Add tests** with xUnit/Shouldly mirroring relevant Python vectors.
4. **Run** `dotnet test` (CI enforces green builds + coverage upload).
5. **Update** docs or samples if behaviour changes.
6. **Submit** a pull request for review.

## 🗺️ Roadmap

### 🎯 Near-Term
- Azure Document Intelligence converter (options already scaffolded)
- Outlook `.msg` ingestion via MIT-friendly dependencies
- Expanded CLI commands (batch mode, globbing, JSON output)
- Richer regression suite mirroring Python test vectors

### 🎯 Future Ideas
- Plugin discovery & sandboxing
- Built-in LLM caption/transcription providers
- Incremental/streaming conversion APIs
- Cloud-native samples (Functions, Containers, Logic Apps)

## 📈 Performance

MarkItDown is designed for high performance with:
- **Stream-based processing** – Avoids writing temporary files by default
- **Async/await everywhere** – Non-blocking I/O with cancellation support
- **Minimal allocations** – Smart buffer reuse and pay-for-play converters
- **Fast detection** – Lightweight sniffing before converter dispatch
- **Extensible hooks** – Offload captions/transcripts to background workers

## 🔧 Configuration

```csharp
var options = new MarkItDownOptions
{
    EnableBuiltins = true,
    EnablePlugins = false,
    ExifToolPath = "/usr/local/bin/exiftool",
    ImageCaptioner = async (bytes, info, token) =>
    {
        // Call your preferred vision or LLM service here
        return await Task.FromResult("A scenic mountain landscape at sunset.");
    },
    AudioTranscriber = async (bytes, info, token) =>
    {
        // Route to speech-to-text provider
        return await Task.FromResult("Welcome to the MarkItDown demo.");
    }
};

var markItDown = new MarkItDown(options);
```

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

This project is a C# conversion of the original [Microsoft MarkItDown](https://github.com/microsoft/markitdown) Python library. The original project was created by the Microsoft AutoGen team.

## 📞 Support

- 📚 **Documentation**: [GitHub Wiki](https://github.com/managedcode/markitdown/wiki)
- 🐛 **Issues**: [GitHub Issues](https://github.com/managedcode/markitdown/issues)
- 💬 **Discussions**: [GitHub Discussions](https://github.com/managedcode/markitdown/discussions)
- 📧 **Email**: Create an issue for support

---

<div align="center">

**[⭐ Star this repository](https://github.com/managedcode/markitdown)** if you find it useful!

Made with ❤️ by [ManagedCode](https://github.com/managedcode)

</div>
