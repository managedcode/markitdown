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
- 🔄 **Disk-first stream processing** - Handle large documents efficiently using managed workspaces instead of `MemoryStream`
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
- [Extended Format Support](#-extended-format-support)
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
⚡ **High Performance** - Stream-friendly, disk-backed buffers prevent large sources from exhausting RAM

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

### 📚 Extended Format Support

| Format | Extension | Status | Description |
|--------|-----------|---------|-------------|
| **DocBook** | `.xml`, `.docbook` | ✅ Supported | Technical documentation with section hierarchy |
| **JATS / NISO** | `.xml` | ✅ Supported | Journal Article Tag Suite articles with enriched metadata |
| **OPML** | `.opml` | ✅ Supported | Outline Processor markup trees converted to Markdown lists |
| **FictionBook (FB2)** | `.fb2` | ✅ Supported | Narrative e-books with cover art and metadata |
| **EndNote XML** | `.xml` | ✅ Supported | Bibliographic exports with citation data |
| **BibTeX** | `.bib`, `.bibtex` | ✅ Supported | Reference entries rendered as Markdown tables |
| **RIS** | `.ris` | ✅ Supported | Research citations emitted with field/value mapping |
| **CSL-JSON** | `.csl.json`, `.json` | ✅ Supported | Citation Style Language exports ready for RAG indexing |
| **LaTeX** | `.tex` | ✅ Supported | Text and math blocks preserved as Markdown or fenced code |
| **reStructuredText** | `.rst` | ✅ Supported | Converts directives, lists, and code blocks |
| **AsciiDoc** | `.adoc`, `.asciidoc` | ✅ Supported | Handles attributes, admonitions, and tables |
| **Org Mode** | `.org` | ✅ Supported | Emacs Org headlines and property drawers |
| **Djot** | `.djot` | ✅ Supported | Djot lightweight markup translation |
| **Typst** | `.typ` | ✅ Supported | Emerging typesetting language support |
| **Textile** | `.textile` | ✅ Supported | Textile markup to Markdown |
| **Wiki Markup** | `.mediawiki`, `.wiki` | ✅ Supported | MediaWiki-style formatting |
| **Mermaid** | `.mmd`, `.mermaid` | ✅ Supported | Diagram source preserved in fenced code blocks |
| **Graphviz DOT** | `.dot` | ✅ Supported | Graph definitions retained for rendering |
| **PlantUML** | `.puml`, `.plantuml` | ✅ Supported | UML diagrams emitted as fenced code |
| **TikZ** | `.tikz` | ✅ Supported | LaTeX TikZ drawings preserved for reuse |
| **MetaMD** | `.metamd`, `.markdown` | ✅ Supported | Round-trips existing MetaMD documents defined in [`docs/MetaMD.md`](docs/MetaMD.md) |

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

### Minimal usage

```csharp
using MarkItDown;

var client = new MarkItDownClient();
await using var result = await client.ConvertAsync("document.pdf");

Console.WriteLine(result.Title);
Console.WriteLine(result.Markdown);
```

### Convert a stream

```csharp
await using var stream = File.OpenRead("invoice.html");
var info = new StreamInfo(extension: ".html", mimeType: "text/html");

await using var result = await client.ConvertAsync(stream, info);
```

### Convert a URL

```csharp
await using var result = await client.ConvertFromUrlAsync("https://contoso.example/blog");
Console.WriteLine(result.Markdown.Length);
```

### Optional Dependencies for Advanced Features
- **PDF Support**: Provided via PdfPig (bundled)
- **Office Documents**: Provided via DocumentFormat.OpenXml (bundled)
- **Image metadata**: Install [ExifTool](https://exiftool.org/) for richer output (`brew install exiftool`, `choco install exiftool`)
- **Image captions**: Supply an `ImageCaptioner` delegate (e.g., calls to an LLM or vision service)
- **Audio transcription**: Supply an `AudioTranscriber` delegate (e.g., Azure Cognitive Services, OpenAI Whisper)

> **Note**: External tools are optional—MarkItDown degrades gracefully when they are absent.

## 💻 Usage

- `ConvertAsync(string path)` converts any supported file on disk and returns a `DocumentConverterResult`.
- `ConvertAsync(Stream stream, StreamInfo info)` handles non-seekable or remote streams once you supply basic metadata (extension/MIME type).
- `ConvertFromUrlAsync(string url)` downloads HTTP(S) content using the optional `HttpClient` you pass into the constructor.
- Always dispose the result (`await using var result = …`) so temporary workspaces and artifacts are cleaned up.
- `DocumentConverterResult` exposes `Markdown`, `Title`, `Segments`, `Artifacts`, and `Metadata` for downstream processing.
- Apply custom behaviour through `MarkItDownOptions` (segment settings, AI providers, middleware) when constructing the client.

### Metadata Keys

The `MetadataKeys` static class centralises every metadata field the converters emit so you never have to guess string names. Use these constants when inspecting `DocumentConverterResult.Metadata`, per-segment metadata, or artifact metadata:

```csharp
await using var client = new MarkItDownClient();
var result = await client.ConvertAsync(path);

if (result.Metadata.TryGetValue(MetadataKeys.DocumentTitle, out var title))
{
    Console.WriteLine($"Detected title: {title}");
}

foreach (var table in result.Artifacts.Tables)
{
    if (table.Metadata.TryGetValue(MetadataKeys.TableComment, out var comment))
    {
        Console.WriteLine(comment);
    }
}
```

Notable keys include `MetadataKeys.TableComment` (table span hints), `MetadataKeys.EmailAttachments` (EML attachment summary), `MetadataKeys.NotebookCellsCount` (Jupyter statistics), and `MetadataKeys.ArchiveEntry` (ZIP entry provenance). Refer to `src/MarkItDown/Utilities/MetadataKeys.cs` for the full catalog; new format handlers add their metadata there so downstream consumers can rely on stable identifiers.

### CLI

Prefer a guided experience? Run the bundled CLI to batch files or URLs:

```bash
dotnet run --project src/MarkItDown.Cli -- path/to/input
```

Use `dotnet publish` with your preferred runtime identifier if you need a self-contained binary.

Each run now surfaces the document title plus quick stats (pages, images, tables, attachments) in the conversion summary. These numbers come straight from `MetadataKeys` so the CLI mirrors what you see when processing results programmatically.

#### Cloud Provider Configuration Prompts

Choose **Configure cloud providers** in the CLI to register AI integrations without writing code. The prompts map directly to the corresponding option objects:

- **Azure** → `AzureIntelligenceOptions` (`DocumentIntelligence`, `Vision`, `Media`) and supports endpoints, API keys/tokens, and Video Indexer account metadata.
- **Google** → `GoogleIntelligenceOptions` with credentials for Vertex AI or Speech services.
- **AWS** → `AwsIntelligenceOptions` for Rekognition/Transcribe style integrations.

You can leave a prompt blank to keep the current value, or enter `-` to clear it. The saved settings are applied to every subsequent conversion until you change them or use **Clear all**. Combine these prompts with the metadata counts above to validate that enrichment providers are wired up correctly.

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

`MarkItDownClient` emits structured `ILogger` events and OpenTelemetry spans by default. Toggle instrumentation with `MarkItDownOptions.EnableTelemetry`, supply a custom `ActivitySource`/`Meter`, or provide a `LoggerFactory` to integrate with your application's logging pipeline.

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
          AccountName = configuration["Azure:VideoIndexer:AccountName"],
          Location = configuration["Azure:VideoIndexer:Location"],
          SubscriptionId = configuration["Azure:VideoIndexer:SubscriptionId"],
          ResourceGroup = configuration["Azure:VideoIndexer:ResourceGroup"],
          ResourceId = configuration["Azure:VideoIndexer:ResourceId"],
          ArmAccessToken = configuration.GetConnectionString("AzureVideoIndexerArmToken")
      }
  };
  ```

- **Managed identity**: omit the `ApiKey`/`ArmAccessToken` properties and the providers automatically fall back to `DefaultAzureCredential`. Assign the managed identity the *Cognitive Services User* role for Document Intelligence and Vision, and follow the [Video Indexer managed identity instructions](https://learn.microsoft.com/azure/azure-video-indexer/video-indexer-use-azure-ad) to authorize uploads.
- **Video Indexer tips**: Video uploads require both the Video Indexer account (ID + region) and either the full resource ID or the trio of subscription id/resource group/account name, plus an ARM token or Azure AD identity with `Contributor` access on the Video Indexer resource. The interactive CLI exposes dedicated prompts for these values under “Configure cloud providers”.

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
          AccountName = "<video-indexer-account-name>",
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

#### YouTube metadata & captions

- **Docs**: [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode) (used under the hood).
- **Out of the box**: `YouTubeUrlConverter` now enriches Markdown with title, channel, stats, thumbnails, and (when available) auto-generated captions laid out as timecoded segments.
- **Custom provider**: supply `MarkItDownOptions.YouTubeMetadataProvider` to disable network access, inject caching, or swap to an alternative implementation.

  ```csharp
  var options = new MarkItDownOptions
  {
      YouTubeMetadataProvider = new YoutubeExplodeMetadataProvider(), // default
      // You can plug in a stub or caching decorator instead:
      // YouTubeMetadataProvider = new MyCachedYouTubeProvider(inner: new YoutubeExplodeMetadataProvider())
  };
  ```

  When a provider returns `null` the converter falls back to URL-derived metadata, so YouTube support remains fully optional.

For LLM-style post-processing, assign `MarkItDownOptions.AiModels` with an `IAiModelProvider`. The built-in `StaticAiModelProvider` accepts `Microsoft.Extensions.AI` clients (chat models, speech-to-text, etc.), enabling you to share application-wide model builders.

### Converter Priority & Detection

- Priority-based dispatch (lower values processed first)
- Automatic stream sniffing via `StreamInfoGuesser`
- Manual overrides via `MarkItDownOptions` or `StreamInfo`

## 🚨 Error Handling & Troubleshooting

### Common Exceptions

```csharp
using MarkItDown;

var markItDown = new MarkItDownClient();

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

var markItDown = new MarkItDownClient(httpClient: httpClient);
```

**Logging for Diagnostics:**
```csharp
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(builder => 
    builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

var logger = loggerFactory.CreateLogger<MarkItDown>();
var markItDown = new MarkItDownClient(logger: logger);

// Now you'll see detailed conversion progress in console output
```

## 🔄 Development & Contributing

### Migration from Python MarkItDown

If you're familiar with the original Python library, here are the key differences:

| Python | C#/.NET | Notes |
|---------|---------|--------|
| `MarkItDownClient()` | `new MarkItDownClient()` | Similar constructor |
| `markitdown.convert("file.pdf")` | `await markItDown.ConvertAsync("file.pdf")` | Async pattern |
| `markitdown.convert(stream, file_extension=".pdf")` | `await markItDown.ConvertAsync(stream, streamInfo)` | StreamInfo object |
| `markitdown.convert_url("https://...")` | `await markItDown.ConvertFromUrlAsync("https://...")` | Async URL conversion |
| `llm_client=...` parameter | `ImageCaptioner`, `AudioTranscriber` delegates | More flexible callback system |
| Plugin system | Not yet implemented | Planned for future release |

**Example Migration:**

```python
# Python version
import markitdown
md = markitdown.MarkItDownClient()
result = md.convert("document.pdf")
print(result.text_content)
```

```csharp
// C# version  
using MarkItDown;
var markItDown = new MarkItDownClient();
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

> 🐳 Several storage regression tests spin up [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) via Testcontainers; ensure Docker is available locally or the suite will skip those checks.

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
var markItDown = new MarkItDownClient();
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
var markItDown = new MarkItDownClient(httpClient: httpClient);

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

var markItDown = new MarkItDownClient(options);
```

### Workspace Storage & Privacy

By default every conversion writes to a unique folder under `.markitdown/` in the current working directory (for example `/app/.markitdown/...`). Those workspaces hold the copied source file, extracted artifacts, and emitted Markdown until the `DocumentConverterResult` is disposed, at which point the directory is deleted. This keeps conversions isolated without leaking data into global temp folders.

You can redirect the workspace to another location—such as the OS temp directory—and opt to keep it after conversion by supplying custom storage options:

```csharp
var workspaceRoot = Path.Combine(Path.GetTempPath(), "markitdown", "workspaces");

var options = new MarkItDownOptions
{
    ArtifactStorage = ArtifactStorageOptions.Default with
    {
        WorkspacePathFormatter = name => Path.Combine(workspaceRoot, name),
        DeleteOnDispose = false    // keep the workspace directory after conversion
    },
    SegmentOptions = SegmentOptions.Default with
    {
        Image = SegmentOptions.Default.Image with
        {
            KeepArtifactDirectory = true
        }
    }
};

Directory.CreateDirectory(workspaceRoot);

await using var client = new MarkItDownClient(options);
await using var result = await client.ConvertAsync("policy.pdf");
```

When you override the workspace root, ensure you manage retention (for example rotate or clean the custom directory) to avoid unbounded growth.

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

var markItDown = new MarkItDownClient(options);
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

var markItDown = new MarkItDownClient(options);
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

var markItDown = new MarkItDownClient(options, logger, httpClientFactory.CreateClient());
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
