# MarkItDown

[![.NET](https://img.shields.io/badge/.NET-9.0+-blue)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![NuGet](https://img.shields.io/nuget/v/ManagedCode.MarkItDown.svg)](https://www.nuget.org/packages/ManagedCode.MarkItDown)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

🚀 **Transform any document into LLM-ready Markdown with this powerful C#/.NET library!**

MarkItDown is a comprehensive document conversion library that transforms diverse file formats (HTML, PDF, DOCX, XLSX, EPUB, archives, URLs, and more) into clean, high-quality Markdown. Perfect for AI workflows, RAG (Retrieval-Augmented Generation) systems, content processing pipelines, and text analytics applications.

**Why MarkItDown for .NET?**
- 🎯 **Built for modern C# developers** - Native .NET 9 library with async/await throughout
- 🧠 **LLM-optimized output** - Clean Markdown that AI models love to consume  
- 📦 **Zero-friction NuGet package** - Just `dotnet add package ManagedCode.MarkItDown` and go
- 🔄 **Stream-based processing** - Handle large documents efficiently without temporary files
- 🛠️ **Highly extensible** - Add custom converters or integrate with AI services for captions/transcription

This is a high-fidelity C# port of Microsoft's original [MarkItDown Python library](https://github.com/microsoft/markitdown), reimagined for the .NET ecosystem with modern async patterns, improved performance, and enterprise-ready features.

## 🌟 Why Choose MarkItDown?

### For AI & LLM Applications
- **Perfect for RAG systems** - Convert documents to searchable, contextual Markdown chunks
- **Token-efficient** - Clean output maximizes your LLM token budget
- **Structured data preservation** - Tables, headers, and lists maintain semantic meaning
- **Metadata extraction** - Rich document properties for enhanced context

### For .NET Developers  
- **Native performance** - Built from the ground up for .NET, not a wrapper
- **Modern async/await** - Non-blocking I/O with full cancellation support
- **Memory efficient** - Stream-based processing avoids loading entire files into memory
- **Enterprise ready** - Proper error handling, logging, and configuration options

### For Content Processing
- **22+ file formats supported** - From Office documents to web pages to archives
- **Batch processing ready** - Handle hundreds of documents efficiently
- **Extensible architecture** - Add custom converters for proprietary formats
- **Smart format detection** - Automatic MIME type and encoding detection

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
🧩 **Conversion middleware** - Compose post-processing steps with `IConversionMiddleware` (AI enrichment ready)
📂 **Raw artifacts API** - Inspect text blocks, tables, and images via `DocumentConverterResult.Artifacts`
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
| **Email** | `.eml` | ✅ Supported | Email files with headers, content, and attachment info |
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
- Page snapshot artifacts ensure every page can be sent through AI enrichment (OCR, diagram-to-Mermaid, chart narration) even when the PDF exposes selectable text

### Office Documents (DOCX/XLSX/PPTX)
- **Word (.docx)**: Headers, paragraphs, tables, bold/italic formatting, and embedded images captured for AI enrichment (OCR, Mermaid-ready diagrams)
- **Excel (.xlsx)**: Spreadsheet data as Markdown tables with sheet organization
- **PowerPoint (.pptx)**: Slide-by-slide content with title recognition plus image artifacts primed for detailed AI captions and diagrams

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
<PackageReference Include="ManagedCode.MarkItDown" Version="0.0.3" />
```

### Prerequisites
- .NET 9.0 SDK or later
- Compatible with .NET 9 apps and libraries

### 🏃‍♂️ 60-Second Quick Start

```csharp
using MarkItDown;

// Create converter instance
var markItDown = new MarkItDown();

// Convert any file to Markdown
var result = await markItDown.ConvertAsync("document.pdf");
Console.WriteLine(result.Markdown);

// That's it! MarkItDown handles format detection automatically
```

### 📚 Real-World Examples

**RAG System Document Ingestion**
```csharp
using MarkItDown;
using Microsoft.Extensions.Logging;

// Set up logging to track conversion progress
using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var logger = loggerFactory.CreateLogger<MarkItDown>();
var markItDown = new MarkItDown(logger: logger);

// Convert documents for vector database ingestion
string[] documents = { "report.pdf", "data.xlsx", "webpage.html" };
var markdownChunks = new List<string>();

foreach (var doc in documents)
{
    try 
    {
        var result = await markItDown.ConvertAsync(doc);
        markdownChunks.Add($"# Document: {result.Title ?? Path.GetFileName(doc)}\n\n{result.Markdown}");
        logger.LogInformation("Converted {Document} ({Length} characters)", doc, result.Markdown.Length);
    }
    catch (UnsupportedFormatException ex)
    {
        logger.LogWarning("Skipped unsupported file {Document}: {Error}", doc, ex.Message);
    }
}

// markdownChunks now ready for embedding and vector storage
```

**Batch Email Processing**
```csharp
using MarkItDown;

var markItDown = new MarkItDown();
var emailFolder = @"C:\Emails\Exports";
var outputFolder = @"C:\ProcessedEmails";

await foreach (var emlFile in Directory.EnumerateFiles(emailFolder, "*.eml").ToAsyncEnumerable())
{
    var result = await markItDown.ConvertAsync(emlFile);
    
    // Extract metadata
    Console.WriteLine($"Email: {result.Title}");
    Console.WriteLine($"Converted to {result.Markdown.Length} characters of Markdown");
    
    // Save processed version
    var outputPath = Path.Combine(outputFolder, Path.ChangeExtension(Path.GetFileName(emlFile), ".md"));
    await File.WriteAllTextAsync(outputPath, result.Markdown);
}
```

**Web Content Processing**
```csharp
using MarkItDown;
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
using var httpClient = new HttpClient();

var markItDown = new MarkItDown(
    logger: loggerFactory.CreateLogger<MarkItDown>(),
    httpClient: httpClient);

// Convert web pages directly
var urls = new[] 
{
    "https://en.wikipedia.org/wiki/Machine_learning",
    "https://docs.microsoft.com/en-us/dotnet/csharp/",
    "https://github.com/microsoft/semantic-kernel"
};

foreach (var url in urls)
{
    var result = await markItDown.ConvertFromUrlAsync(url);
    Console.WriteLine($"📄 {result.Title}");
    Console.WriteLine($"🔗 Source: {url}");
    Console.WriteLine($"📝 Content: {result.Markdown.Length} characters");
    Console.WriteLine("---");
}
```

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

### Convert email files (EML)

```csharp
using MarkItDown;

// Convert an EML file to Markdown
var markItDown = new MarkItDown();
DocumentConverterResult result = await markItDown.ConvertAsync("message.eml");

// The result includes email headers and content
Console.WriteLine($"Subject: {result.Title}");
Console.WriteLine(result.Markdown);
// Output includes:
// # Email
// **Subject:** Important Project Update
// **From:** sender@example.com
// **To:** recipient@example.com
// **Date:** 2024-01-15 10:30:00 +00:00
// 
// ## Message Content
// [Email body content converted to Markdown]
// 
// ## Attachments (if any)
// - file.pdf (application/pdf) - 1.2 MB
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
using System;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Providers.Azure;
using MarkItDown.Intelligence.Providers.Google;
using MarkItDown.Intelligence.Providers.Aws;

var options = new MarkItDownOptions
{
    Segments = new SegmentOptions
    {
        IncludeSegmentMetadataInMarkdown = true,
        Audio = new AudioSegmentOptions
        {
            SegmentDuration = TimeSpan.FromMinutes(2)
        }
    },

    // Cloud providers can be wired in through the intelligence options.
    AzureIntelligence = new AzureIntelligenceOptions
    {
        DocumentIntelligence = new AzureDocumentIntelligenceOptions
        {
            Endpoint = "https://<your-document-intelligence>.cognitiveservices.azure.com/",
            ApiKey = "<document-intelligence-key>",
            ModelId = "prebuilt-layout"
        },
        Vision = new AzureVisionOptions
        {
            Endpoint = "https://<your-vision>.cognitiveservices.azure.com/",
            ApiKey = "<vision-key>"
        },
        Media = new AzureMediaIntelligenceOptions
        {
            AccountId = "<video-indexer-account-id>",
            Location = "trial",
            SubscriptionId = "<subscription-id>",
            ResourceGroup = "<resource-group>"
        }
    },

    GoogleIntelligence = new GoogleIntelligenceOptions
    {
        DocumentIntelligence = new GoogleDocumentIntelligenceOptions
        {
            ProjectId = "my-project",
            Location = "us",
            ProcessorId = "<processor-id>",
            CredentialsPath = "google-sa.json"
        },
        Vision = new GoogleVisionOptions
        {
            CredentialsPath = "google-sa.json",
            MaxLabels = 5
        },
        Media = new GoogleMediaIntelligenceOptions
        {
            CredentialsPath = "google-sa.json",
            LanguageCode = "en-US"
        }
    },

    AwsIntelligence = new AwsIntelligenceOptions
    {
        DocumentIntelligence = new AwsDocumentIntelligenceOptions
        {
            Region = "us-east-1"
        },
        Vision = new AwsVisionOptions
        {
            Region = "us-east-1",
            MinConfidence = 80f
        },
        Media = new AwsMediaIntelligenceOptions
        {
            Region = "us-east-1",
            InputBucketName = "my-transcribe-input",
            OutputBucketName = "my-transcribe-output"
        }
    }
};

var markItDown = new MarkItDown(options);

// Segments are still available programmatically even when annotations are disabled.
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

## 🎯 Advanced Usage Patterns

### Custom Format Converters

```csharp
using MarkItDown;

public class PowerBIConverter : IDocumentConverter
{
    public int Priority => 150; // Between HTML and PlainText

    public bool AcceptsInput(StreamInfo streamInfo) =>
        streamInfo.Extension?.ToLowerInvariant() == ".pbix" ||
        streamInfo.MimeType?.Contains("powerbi") == true;

    public async Task<DocumentConverterResult> ConvertAsync(
        Stream stream, 
        StreamInfo streamInfo, 
        CancellationToken cancellationToken = default)
    {
        // Custom PowerBI file processing logic here
        var markdown = await ProcessPowerBIFile(stream, cancellationToken);
        return new DocumentConverterResult(markdown, "PowerBI Report");
    }
    
    private async Task<string> ProcessPowerBIFile(Stream stream, CancellationToken cancellationToken)
    {
        // Implementation details...
        await Task.Delay(100, cancellationToken); // Placeholder
        return "# PowerBI Report\n\nProcessed PowerBI content here...";
    }
}
```

### Batch Processing with Progress Tracking

```csharp
using MarkItDown;
using Microsoft.Extensions.Logging;

public class DocumentProcessor
{
    private readonly MarkItDown _markItDown;
    private readonly ILogger<DocumentProcessor> _logger;

    public DocumentProcessor(ILogger<DocumentProcessor> logger)
    {
        _logger = logger;
        _markItDown = new MarkItDown(logger: logger);
    }

    public async Task<List<ProcessedDocument>> ProcessDirectoryAsync(
        string directoryPath, 
        string outputPath,
        IProgress<ProcessingProgress>? progress = null)
    {
        var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith('.'))
            .ToList();

        var results = new List<ProcessedDocument>();
        var processed = 0;

        await Parallel.ForEachAsync(files, new ParallelOptions 
        { 
            MaxDegreeOfParallelism = Environment.ProcessorCount 
        },
        async (file, cancellationToken) =>
        {
            try
            {
                var result = await _markItDown.ConvertAsync(file, cancellationToken: cancellationToken);
                var outputFile = Path.Combine(outputPath, 
                    Path.ChangeExtension(Path.GetRelativePath(directoryPath, file), ".md"));
                
                Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
                await File.WriteAllTextAsync(outputFile, result.Markdown, cancellationToken);
                
                lock (results)
                {
                    results.Add(new ProcessedDocument(file, outputFile, result.Markdown.Length));
                    processed++;
                    progress?.Report(new ProcessingProgress(processed, files.Count, file));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process {File}", file);
            }
        });

        return results;
    }
}

public record ProcessedDocument(string InputPath, string OutputPath, int CharacterCount);
public record ProcessingProgress(int Processed, int Total, string CurrentFile);
```

### Integration with Vector Databases

```csharp
using MarkItDown;
using Microsoft.Extensions.VectorData;

public class DocumentIndexer
{
    private readonly MarkItDown _markItDown;
    private readonly IVectorStore _vectorStore;

    public DocumentIndexer(IVectorStore vectorStore)
    {
        _vectorStore = vectorStore;
        _markItDown = new MarkItDown();
    }

    public async Task IndexDocumentAsync<T>(string filePath) where T : class
    {
        // Convert to Markdown
        var result = await _markItDown.ConvertAsync(filePath);
        
        // Split into chunks for better vector search
        var chunks = SplitIntoChunks(result.Markdown, maxChunkSize: 500);
        
        var collection = _vectorStore.GetCollection<T>("documents");
        
        for (int i = 0; i < chunks.Count; i++)
        {
            var document = new DocumentChunk
            {
                Id = $"{Path.GetFileName(filePath)}_{i}",
                Content = chunks[i],
                Title = result.Title ?? Path.GetFileName(filePath),
                Source = filePath,
                ChunkIndex = i
            };

            await collection.UpsertAsync(document);
        }
    }
    
    private List<string> SplitIntoChunks(string markdown, int maxChunkSize)
    {
        // Smart chunking logic that preserves markdown structure
        var chunks = new List<string>();
        var lines = markdown.Split('\n');
        var currentChunk = new StringBuilder();
        
        foreach (var line in lines)
        {
            if (currentChunk.Length + line.Length > maxChunkSize && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
                currentChunk.Clear();
            }
            currentChunk.AppendLine(line);
        }
        
        if (currentChunk.Length > 0)
            chunks.Add(currentChunk.ToString().Trim());
            
        return chunks;
    }
}

public class DocumentChunk
{
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";
    public string Title { get; set; } = "";
    public string Source { get; set; } = "";
    public int ChunkIndex { get; set; }
}
```

### Cloud Function Integration

```csharp
// Azure Functions example
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MarkItDown;

public class DocumentConversionFunction
{
    private readonly MarkItDown _markItDown;
    private readonly ILogger<DocumentConversionFunction> _logger;

    public DocumentConversionFunction(ILogger<DocumentConversionFunction> logger)
    {
        _logger = logger;
        _markItDown = new MarkItDown(logger: logger);
    }

    [Function("ConvertDocument")]
    public async Task<HttpResponseData> ConvertDocument(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        try
        {
            var formData = await req.ReadFormAsync();
            var file = formData.Files.FirstOrDefault();
            
            if (file == null)
            {
                var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("No file uploaded");
                return badResponse;
            }

            var streamInfo = new StreamInfo(
                extension: Path.GetExtension(file.FileName),
                fileName: file.FileName,
                mimeType: file.ContentType
            );

            var result = await _markItDown.ConvertAsync(file.OpenReadStream(), streamInfo);
            
            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            
            await response.WriteAsJsonAsync(new 
            { 
                title = result.Title,
                markdown = result.Markdown,
                characterCount = result.Markdown.Length
            });
            
            return response;
        }
        catch (UnsupportedFormatException ex)
        {
            var response = req.CreateResponse(System.Net.HttpStatusCode.UnsupportedMediaType);
            await response.WriteStringAsync($"Unsupported file format: {ex.Message}");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document conversion failed");
            var response = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
            await response.WriteStringAsync("Internal server error");
            return response;
        }
    }
}
```

## 🏗️ Architecture

### Core Components

- **`MarkItDown`** - Main entry point for conversions
- **`IDocumentConverter`** - Interface for format-specific converters
- **`DocumentConverterResult`** - Contains the aggregate Markdown plus structured `DocumentSegment` entries
- **`StreamInfo`** - Metadata about the input stream (MIME type, extension, charset, etc.)
- **`ConverterRegistration`** - Associates converters with priority for selection

> ℹ️ MIME detection and normalization rely on [ManagedCode.MimeTypes](https://github.com/managedcode/MimeTypes).

### Built-in Converters

MarkItDown includes these converters in priority order:

- **`YouTubeUrlConverter`** - Video metadata from YouTube URLs
- **`HtmlConverter`** - HTML to Markdown using AngleSharp
- **`WikipediaConverter`** - Clean article extraction from Wikipedia pages
- **`BingSerpConverter`** - Search result summaries from Bing
- **`RssFeedConverter`** - RSS/Atom feeds with article processing
- **`JsonConverter`** - Structured JSON data with formatting
- **`JupyterNotebookConverter`** - Python notebooks with code and markdown cells
- **`CsvConverter`** - CSV files as Markdown tables
- **`EpubConverter`** - E-book content and metadata
- **`EmlConverter`** - Email files with headers and attachments
- **`XmlConverter`** - XML documents with structure preservation
- **`ZipConverter`** - Archive processing with recursive conversion
- **`PdfConverter`** - PDF text extraction using PdfPig
- **`DocxConverter`** - Microsoft Word documents
- **`XlsxConverter`** - Microsoft Excel spreadsheets 
- **`PptxConverter`** - Microsoft PowerPoint presentations
- **`AudioConverter`** - Audio metadata and optional transcription
- **`ImageConverter`** - Image metadata via ExifTool and optional captions
- **`PlainTextConverter`** - Plain text, Markdown, and other text formats (fallback)

### Structured Segments & Metadata

Every conversion populates `DocumentConverterResult.Segments` with strongly typed `DocumentSegment` instances. Segments preserve natural breakpoints (pages, slides, sheets, archive entries, audio ranges) alongside rich metadata:

- `Type` and `Number` expose what the segment represents (for example page/slide numbers)
- `Label` carries human-readable descriptors when available
- `StartTime`/`EndTime` capture media timelines for audio/video content
- `AdditionalMetadata` holds contextual properties such as archive entry paths or sheet names

```csharp
var result = await markItDown.ConvertAsync("report.pdf");

foreach (var segment in result.Segments)
{
    Console.WriteLine($"[{segment.Type}] #{segment.Number}: {segment.Label}");
}
```

Runtime behaviour is controlled through `SegmentOptions` on `MarkItDownOptions`. Enabling `IncludeSegmentMetadataInMarkdown` emits inline annotations like `[page:1]`, `[sheet:Sales]`, or `[timecode:00:01:00-00:02:00]` directly in the Markdown stream. Audio transcripts honour `Segments.Audio.SegmentDuration`, while still collapsing short transcripts into a single, time-aware slice.

### Cloud Intelligence Providers

MarkItDown exposes optional abstractions for running documents through cloud services:

- `IDocumentIntelligenceProvider` – structured page, table, and layout extraction.
- `IImageUnderstandingProvider` – OCR, captioning, and object detection for embedded images.
- `IMediaTranscriptionProvider` – timed transcripts for audio and video inputs.

The `AzureIntelligenceOptions`, `GoogleIntelligenceOptions`, and `AwsIntelligenceOptions` helpers wire the respective cloud Document AI/Vision/Speech stacks without forcing the dependency on consumers. You can still bring your own implementation by assigning the provider interfaces directly on `MarkItDownOptions`.

#### Azure AI setup (keys and managed identity)

- **Docs**: [Document Intelligence](https://learn.microsoft.com/azure/ai-services/document-intelligence/), [Computer Vision Image Analysis](https://learn.microsoft.com/azure/ai-services/computer-vision/overview-image-analysis), [Video Indexer authentication](https://learn.microsoft.com/azure/azure-video-indexer/video-indexer-get-started/connect-to-azure).
- **API keys / connection strings**: store your Cognitive Services key in configuration (for example `appsettings.json` or an Azure App Configuration connection string) and hydrate the options:

  ```csharp
  var configuration = host.Services.GetRequiredService<IConfiguration>();

  var azureOptions = new AzureIntelligenceOptions
  {
      DocumentIntelligence = new AzureDocumentIntelligenceOptions
      {
          Endpoint = configuration["Azure:DocumentIntelligence:Endpoint"],
          ApiKey = configuration.GetConnectionString("AzureDocumentIntelligenceKey"),
          ModelId = "prebuilt-layout"
      },
      Vision = new AzureVisionOptions
      {
          Endpoint = configuration["Azure:Vision:Endpoint"],
          ApiKey = configuration.GetConnectionString("AzureVisionKey")
      },
      Media = new AzureMediaIntelligenceOptions
      {
          AccountId = configuration["Azure:VideoIndexer:AccountId"],
          Location = configuration["Azure:VideoIndexer:Location"],
          SubscriptionId = configuration["Azure:VideoIndexer:SubscriptionId"],
          ResourceGroup = configuration["Azure:VideoIndexer:ResourceGroup"],
          ArmAccessToken = configuration.GetConnectionString("AzureVideoIndexerArmToken")
      }
  };
  ```

- **Managed identity**: omit the `ApiKey`/`ArmAccessToken` properties and the providers automatically fall back to `DefaultAzureCredential`. Assign the managed identity the *Cognitive Services User* role for Document Intelligence and Vision, and follow the [Video Indexer managed identity instructions](https://learn.microsoft.com/azure/azure-video-indexer/video-indexer-use-azure-ad) to authorize uploads.

  ```csharp
  var azureOptions = new AzureIntelligenceOptions
  {
      DocumentIntelligence = new AzureDocumentIntelligenceOptions
      {
          Endpoint = "https://contoso.cognitiveservices.azure.com/"
      },
      Vision = new AzureVisionOptions
      {
          Endpoint = "https://contoso.cognitiveservices.azure.com/"
      },
      Media = new AzureMediaIntelligenceOptions
      {
          AccountId = "<video-indexer-account-id>",
          Location = "trial"
      }
  };
  ```

#### Google Cloud setup

- **Docs**: [Document AI](https://cloud.google.com/document-ai/docs), [Vision API](https://cloud.google.com/vision/docs), [Speech-to-Text](https://cloud.google.com/speech-to-text/docs).
- **Service account JSON / ADC**: place your service account JSON on disk or load it from Secret Manager, then point the options at it (or provide a `GoogleCredential` instance). If `CredentialsPath`/`JsonCredentials`/`Credential` are omitted the providers use [Application Default Credentials](https://cloud.google.com/docs/authentication/provide-credentials-adc#local-key):

  ```csharp
  var googleOptions = new GoogleIntelligenceOptions
  {
      DocumentIntelligence = new GoogleDocumentIntelligenceOptions
      {
          ProjectId = "my-project",
          Location = "us",
          ProcessorId = "processor-id",
          CredentialsPath = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")
      },
      Vision = new GoogleVisionOptions
      {
          JsonCredentials = Environment.GetEnvironmentVariable("GOOGLE_VISION_JSON")
      },
      Media = new GoogleMediaIntelligenceOptions
      {
          Credential = GoogleCredential.GetApplicationDefault(),
          LanguageCode = "en-US"
      }
  };
  ```

- **Workload identity / managed identities**: host the app on GKE, Cloud Run, or Cloud Functions with [Workload Identity Federation](https://cloud.google.com/iam/docs/workload-identity-federation). The Google SDK automatic credential chain will pick up the ambient identity and the providers will work without JSON keys.

#### AWS setup

- **Docs**: [Textract](https://docs.aws.amazon.com/textract/latest/dg/what-is.html), [Rekognition](https://docs.aws.amazon.com/rekognition/latest/dg/what-is.html), [Transcribe](https://docs.aws.amazon.com/transcribe/latest/dg/what-is-transcribe.html), [.NET credential management](https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/net-dg-config-creds.html).
- **Access keys / connection strings**: populate the options directly from configuration when you must supply static credentials (for example from AWS Secrets Manager or an encrypted connection string):

  ```csharp
  var awsOptions = new AwsIntelligenceOptions
  {
      DocumentIntelligence = new AwsDocumentIntelligenceOptions
      {
          AccessKeyId = configuration["AWS:AccessKeyId"],
          SecretAccessKey = configuration["AWS:SecretAccessKey"],
          Region = configuration.GetValue<string>("AWS:Region")
      },
      Vision = new AwsVisionOptions
      {
          AccessKeyId = configuration["AWS:AccessKeyId"],
          SecretAccessKey = configuration["AWS:SecretAccessKey"],
          Region = configuration.GetValue<string>("AWS:Region"),
          MinConfidence = 80f
      },
      Media = new AwsMediaIntelligenceOptions
      {
          AccessKeyId = configuration["AWS:AccessKeyId"],
          SecretAccessKey = configuration["AWS:SecretAccessKey"],
          Region = configuration.GetValue<string>("AWS:Region"),
          InputBucketName = configuration["AWS:Transcribe:InputBucket"],
          OutputBucketName = configuration["AWS:Transcribe:OutputBucket"]
      }
  };
  ```

- **IAM roles / AWS managed identity**: leave the credential fields null to use the default AWS credential chain (environment variables, shared credentials file, EC2/ECS/EKS IAM roles, or AWS SSO). Ensure the execution role has permissions for `textract:AnalyzeDocument`, `rekognition:DetectLabels`, `rekognition:DetectText`, `transcribe:StartTranscriptionJob`, and S3 access for the specified buckets.

For LLM-style post-processing, assign `MarkItDownOptions.AiModels` with an `IAiModelProvider`. The built-in `StaticAiModelProvider` accepts `Microsoft.Extensions.AI` clients (chat models, speech-to-text, etc.), enabling you to share application-wide model builders.

### Converter Priority & Detection

- Priority-based dispatch (lower values processed first)
- Automatic stream sniffing via `StreamInfoGuesser`
- Manual overrides via `MarkItDownOptions` or `StreamInfo`

## 🚨 Error Handling & Troubleshooting

### Common Exceptions

```csharp
using MarkItDown;

var markItDown = new MarkItDown();

try
{
    var result = await markItDown.ConvertAsync("document.pdf");
    Console.WriteLine(result.Markdown);
}
catch (UnsupportedFormatException ex)
{
    // File format not supported by any converter
    Console.WriteLine($"Cannot process this file type: {ex.Message}");
}
catch (FileNotFoundException ex)
{
    // File path doesn't exist
    Console.WriteLine($"File not found: {ex.Message}");
}
catch (UnauthorizedAccessException ex)
{
    // Permission issues
    Console.WriteLine($"Access denied: {ex.Message}");
}
catch (MarkItDownException ex)
{
    // General conversion errors (corrupt files, parsing issues, etc.)
    Console.WriteLine($"Conversion failed: {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"Details: {ex.InnerException.Message}");
}
```

### Troubleshooting Tips

**File Format Detection Issues:**
```csharp
// Force specific format detection
var streamInfo = new StreamInfo(
    mimeType: "application/pdf",  // Explicit MIME type
    extension: ".pdf",            // Explicit extension
    fileName: "document.pdf"      // Original filename
);

var result = await markItDown.ConvertAsync(stream, streamInfo);
```

**Memory Issues with Large Files:**
```csharp
// Use cancellation tokens to prevent runaway processing
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

try 
{
    var result = await markItDown.ConvertAsync("large-file.pdf", cancellationToken: cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Conversion timed out - file may be too large or complex");
}
```

**Network Issues (URLs):**
```csharp
// Configure HttpClient for better reliability
using var httpClient = new HttpClient();
httpClient.Timeout = TimeSpan.FromSeconds(30);
httpClient.DefaultRequestHeaders.Add("User-Agent", "MarkItDown/1.0");

var markItDown = new MarkItDown(httpClient: httpClient);
```

**Logging for Diagnostics:**
```csharp
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(builder => 
    builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

var logger = loggerFactory.CreateLogger<MarkItDown>();
var markItDown = new MarkItDown(logger: logger);

// Now you'll see detailed conversion progress in console output
```

## 🔄 Development & Contributing

### Migration from Python MarkItDown

If you're familiar with the original Python library, here are the key differences:

| Python | C#/.NET | Notes |
|---------|---------|--------|
| `MarkItDown()` | `new MarkItDown()` | Similar constructor |
| `markitdown.convert("file.pdf")` | `await markItDown.ConvertAsync("file.pdf")` | Async pattern |
| `markitdown.convert(stream, file_extension=".pdf")` | `await markItDown.ConvertAsync(stream, streamInfo)` | StreamInfo object |
| `markitdown.convert_url("https://...")` | `await markItDown.ConvertFromUrlAsync("https://...")` | Async URL conversion |
| `llm_client=...` parameter | `ImageCaptioner`, `AudioTranscriber` delegates | More flexible callback system |
| Plugin system | Not yet implemented | Planned for future release |

**Example Migration:**

```python
# Python version
import markitdown
md = markitdown.MarkItDown()
result = md.convert("document.pdf")
print(result.text_content)
```

```csharp
// C# version  
using MarkItDown;
var markItDown = new MarkItDown();
var result = await markItDown.ConvertAsync("document.pdf");
Console.WriteLine(result.Markdown);
```

### .NET SDK Setup

MarkItDown targets .NET 9.0. If your environment does not have the required SDK, run the helper script once:

```bash
./eng/install-dotnet.sh
```

The script installs the SDK into `~/.dotnet` using the official `dotnet-install` bootstrapper and prints the environment
variables to add to your shell profile so the `dotnet` CLI is available on subsequent sessions.

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

> ✅ The regression suite now exercises DOCX and PPTX conversions with embedded imagery, ensuring conversion middleware runs and enriched descriptions remain attached to the composed Markdown.
>
> ✅ Additional image-placement regressions verify that AI-generated captions are injected immediately after each source placeholder for DOCX, PPTX, and PDF outputs.

### Project Structure

```
├── src/
│   └── MarkItDown/                 # Core library
│       ├── Converters/             # Format-specific converters (HTML, PDF, audio, etc.)
│       ├── MarkItDown.cs          # Main conversion engine
│       ├── StreamInfoGuesser.cs   # MIME/charset/extension detection helpers
│       ├── MarkItDownOptions.cs   # Runtime configuration flags
│       └── ...                    # Shared utilities (UriUtilities, MimeMapping, etc.)
├── tests/
│   └── MarkItDown.Tests/          # xUnit + Shouldly tests, Python parity vectors
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
- Performance optimizations and memory usage improvements
- Enhanced test coverage mirroring Python test vectors

### 🎯 Future Ideas
- Plugin discovery & sandboxing for custom converters
- Built-in LLM caption/transcription providers (OpenAI, Azure AI)
- Incremental/streaming conversion APIs for large documents
- Cloud-native integration samples (Azure Functions, AWS Lambda)
- Command-line interface (CLI) for batch processing

## 📈 Performance

MarkItDown is designed for high-performance document processing in production environments:

### 🚀 Performance Characteristics

| Feature | Benefit | Impact |
|---------|---------|--------|
| **Stream-based processing** | No temporary files created | Faster I/O, lower disk usage |
| **Async/await throughout** | Non-blocking operations | Better scalability, responsive UIs |
| **Memory efficient** | Smart buffer reuse | Lower memory footprint for large documents |
| **Fast format detection** | Lightweight MIME/extension sniffing | Quick routing to appropriate converter |
| **Parallel processing ready** | Thread-safe converter instances | Handle multiple documents concurrently |

### 📊 Performance Considerations

MarkItDown's performance depends on:
- **Document size and complexity** - Larger files with more formatting take longer to process
- **File format** - Some formats (like PDF) require more processing than others (like plain text)
- **Available system resources** - Memory, CPU, and I/O capabilities
- **Optional services** - Image captioning and audio transcription add processing time

Performance will vary based on your specific documents and environment. For production workloads, we recommend benchmarking with your actual document types and sizes.

### ⚡ Optimization Tips

```csharp
// 1. Reuse MarkItDown instances (they're thread-safe)
var markItDown = new MarkItDown();
await Task.WhenAll(
    markItDown.ConvertAsync("file1.pdf"),
    markItDown.ConvertAsync("file2.docx"),
    markItDown.ConvertAsync("file3.html")
);

// 2. Use cancellation tokens for timeouts
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
var result = await markItDown.ConvertAsync("large-file.pdf", cancellationToken: cts.Token);

// 3. Configure HttpClient for web content (reuse connections)
using var httpClient = new HttpClient();
var markItDown = new MarkItDown(httpClient: httpClient);

// 4. Pre-specify StreamInfo to skip format detection
var streamInfo = new StreamInfo(mimeType: "application/pdf", extension: ".pdf");
var result = await markItDown.ConvertAsync(stream, streamInfo);
```

## 🔧 Configuration

### Basic Configuration

```csharp
var options = new MarkItDownOptions
{
    EnableBuiltins = true,      // Use built-in converters (default: true)
    EnablePlugins = false,      // Plugin system (reserved for future use)
    ExifToolPath = "/usr/local/bin/exiftool"  // Path to exiftool binary (optional)
};

var markItDown = new MarkItDown(options);
```

### Advanced AI Integration

```csharp
using Azure;
using OpenAI;

var openAIChatClient = new MyChatClient(); // IChatClient from Microsoft.Extensions.AI
var whisperSpeechClient = new MySpeechToTextClient(); // ISpeechToTextClient from Microsoft.Extensions.AI

var options = new MarkItDownOptions
{
    AiModels = new StaticAiModelProvider(openAIChatClient, whisperSpeechClient),

    AzureIntelligence = new AzureIntelligenceOptions
    {
        DocumentIntelligence = new AzureDocumentIntelligenceOptions
        {
            Endpoint = "https://your-document-intelligence.cognitiveservices.azure.com/",
            ApiKey = "<document-intelligence-key>"
        },
        Vision = new AzureVisionOptions
        {
            Endpoint = "https://your-computervision.cognitiveservices.azure.com/",
            ApiKey = "<vision-key>"
        }
    }
};

var markItDown = new MarkItDown(options);
```

### Conversion Middleware & Raw Artifacts

Every conversion now exposes the raw extraction artifacts that feed the Markdown composer. Use `DocumentConverterResult.Artifacts` to inspect page text, tables, or embedded images before they are flattened into Markdown. You can plug additional processing by registering `IConversionMiddleware` instances through `MarkItDownOptions.ConversionMiddleware`. Middleware executes after extraction and can mutate segments, enrich metadata, or call external AI services. When an `IChatClient` is supplied and `EnableAiImageEnrichment` remains `true` (default), MarkItDown automatically adds the built-in `AiImageEnrichmentMiddleware` to describe charts, diagrams, and other visuals. The middleware keeps enriched prose anchored to the exact Markdown placeholder emitted during extraction, ensuring captions, Mermaid diagrams, and OCR text land beside the original image instead of drifting to the end of the section.

```csharp
var options = new MarkItDownOptions
{
    AiModels = new StaticAiModelProvider(chatClient: myChatClient, speechToTextClient: null),
    ConversionMiddleware = new IConversionMiddleware[]
    {
        new MyDomainSpecificMiddleware()
    }
};

var markItDown = new MarkItDown(options);
var result = await markItDown.ConvertAsync("docs/diagram.docx");

foreach (var image in result.Artifacts.Images)
{
    Console.WriteLine($"Image {image.Label}: {image.DetailedDescription}");
}
```

Set `EnableAiImageEnrichment` to `false` when you need a completely custom pipeline with no default AI step.

### Production Configuration with Error Handling

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

// Set up dependency injection
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
services.AddHttpClient();

var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILogger<MarkItDown>>();
var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

var options = new MarkItDownOptions
{
    // Graceful degradation for image processing
    ImageCaptioner = async (bytes, info, token) =>
    {
        try
        {
            // Your AI service call here
            return await CallVisionServiceAsync(bytes, token);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Image captioning failed: {Error}", ex.Message);
            return $"[Image: {info.FileName ?? "unknown"}]";  // Fallback
        }
    }
};

var markItDown = new MarkItDown(options, logger, httpClientFactory.CreateClient());
```

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

This project is a comprehensive C# port of the original [Microsoft MarkItDown](https://github.com/microsoft/markitdown) Python library, created by the Microsoft AutoGen team. We've reimagined it specifically for the .NET ecosystem while maintaining compatibility with the original's design philosophy and capabilities.

**Key differences in this .NET version:**
- 🎯 **Native .NET performance** - Built from scratch in C#, not a Python wrapper
- 🔄 **Modern async patterns** - Full async/await support with cancellation tokens
- 📦 **NuGet ecosystem integration** - Easy installation and dependency management
- 🛠️ **Enterprise features** - Comprehensive logging, error handling, and configuration
- 🚀 **Enhanced performance** - Stream-based processing and memory optimizations

**Maintained by:** [ManagedCode](https://github.com/managedcode) team  
**Original inspiration:** Microsoft AutoGen team  
**License:** MIT (same as the original Python version)

We're committed to maintaining feature parity with the upstream Python project while delivering the performance and developer experience that .NET developers expect.

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
