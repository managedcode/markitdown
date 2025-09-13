# MarkItDown

[![.NET](https://img.shields.io/badge/.NET-8.0+-blue)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![NuGet](https://img.shields.io/nuget/v/MarkItDown.svg)](https://www.nuget.org/packages/MarkItDown)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A modern C# .NET library for converting various document formats (HTML, PDF, DOCX, XLSX, etc.) into clean Markdown suitable for Large Language Models (LLMs) and text analysis pipelines. This project is a conversion from the original Python implementation to C# while maintaining API compatibility and adding modern async/await patterns.

## Features

✨ **Modern .NET** - Built with .NET 8.0+, ready for .NET 9  
📦 **NuGet Package** - Easy installation via package manager  
🔄 **Async/Await** - Full async support for better performance  
🧠 **LLM-Optimized** - Output specifically designed for AI processing  
🔧 **Extensible** - Plugin system for custom converters  
⚡ **High Performance** - Stream-based processing, minimal memory usage

## 📋 Format Support

| Format | Extension | Status | Description |
|--------|-----------|---------|-------------|
| **HTML** | `.html`, `.htm` | ✅ Supported | Full HTML to Markdown conversion |
| **Plain Text** | `.txt`, `.md` | ✅ Supported | Direct text processing |
| **PDF** | `.pdf` | ✅ Supported | Adobe PDF documents with text extraction |
| **Word** | `.docx` | ✅ Supported | Microsoft Word documents with formatting |
| **Excel** | `.xlsx` | ✅ Supported | Microsoft Excel spreadsheets as tables |
| **PowerPoint** | `.pptx` | ✅ Supported | Microsoft PowerPoint presentations |
| **Images** | `.jpg`, `.png`, `.gif`, `.bmp`, `.tiff`, `.webp` | ✅ Supported | OCR-based text extraction |
| **CSV** | `.csv` | ✅ Supported | Comma-separated values as Markdown tables |
| **JSON** | `.json`, `.jsonl`, `.ndjson` | ✅ Supported | Structured JSON data with formatting |
| **XML** | `.xml`, `.xsd`, `.xsl`, `.rss`, `.atom` | ✅ Supported | XML documents with structure preservation |
| **EPUB** | `.epub` | ✅ Supported | E-book files with metadata and content |
| **ZIP** | `.zip` | ✅ Supported | Archive processing with recursive file conversion |
| **Jupyter Notebook** | `.ipynb` | ✅ Supported | Python notebooks with code and markdown cells |
| **RSS/Atom Feeds** | `.rss`, `.atom`, `.xml` | ✅ Supported | Web feeds with structured content and metadata |
| **YouTube URLs** | YouTube links | ✅ Supported | Video metadata extraction and link formatting |

### HTML Conversion Features
- Headers (H1-H6) → Markdown headers
- Bold/Strong text → **bold**
- Italic/Emphasis text → *italic*
- Links → [text](url)
- Images → ![alt](src)
- Lists (ordered/unordered)
- Tables with header detection
- Code blocks and inline code
- Blockquotes

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

### Image OCR Features
- Support for multiple formats: JPEG, PNG, GIF, BMP, TIFF, WebP
- Text extraction using Tesseract OCR
- Header detection and paragraph formatting
- Graceful fallback when OCR fails

## 🚀 Quick Start

### Installation

Install via NuGet Package Manager:

```bash
# Package Manager Console
Install-Package MarkItDown

# .NET CLI
dotnet add package MarkItDown

# PackageReference (add to your .csproj)
<PackageReference Include="MarkItDown" Version="1.0.0" />
```

### Prerequisites
- .NET 8.0 SDK or later
- Compatible with .NET 8.0+ projects (ready for .NET 9)

### Optional Dependencies for Advanced Features
- **PDF Support**: Included via iText7 (automatically installed)
- **Office Documents**: Included via DocumentFormat.OpenXml (automatically installed)
- **Image OCR**: Requires Tesseract OCR data files
  - Install Tesseract: `apt-get install tesseract-ocr` (Linux) or `brew install tesseract` (macOS)
  - Set `TESSDATA_PREFIX` environment variable to Tesseract data directory if needed

> **Note**: All dependencies except Tesseract OCR data are automatically managed via NuGet packages.

## 💻 Usage

### Basic API Usage

```csharp
using MarkItDown.Core;

// Simple conversion
var markItDown = new MarkItDown();
var result = await markItDown.ConvertAsync("document.html");
Console.WriteLine(result.Markdown);
```

### Advanced Usage with Logging

```csharp
using MarkItDown.Core;
using Microsoft.Extensions.Logging;

// With logging and HTTP client for web content
using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var logger = loggerFactory.CreateLogger<Program>();

using var httpClient = new HttpClient();
var markItDown = new MarkItDown(logger, httpClient);

// Convert from file
var fileResult = await markItDown.ConvertAsync("document.html");

// Convert from URL
var urlResult = await markItDown.ConvertFromUrlAsync("https://example.com");

// Convert from stream
using var stream = File.OpenRead("document.html");
var streamInfo = new StreamInfo(mimeType: "text/html", extension: ".html");
var streamResult = await markItDown.ConvertAsync(stream, streamInfo);
```

### Custom Converters

Create your own format converters by implementing `IDocumentConverter`:

```csharp
using MarkItDown.Core;

public class MyCustomConverter : IDocumentConverter
{
    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        return streamInfo.Extension == ".mycustomformat";
    }

    public async Task<DocumentConverterResult> ConvertAsync(
        Stream stream, 
        StreamInfo streamInfo, 
        CancellationToken cancellationToken = default)
    {
        // Your conversion logic here
        var markdown = "# Converted from custom format\n\nContent here...";
        return new DocumentConverterResult(markdown, "Document Title");
    }
}

// Register the custom converter
var markItDown = new MarkItDown();
markItDown.RegisterConverter(new MyCustomConverter(), ConverterPriority.SpecificFileFormat);
```

### Command Line Tool

The package also includes a command-line tool for batch processing:

```bash
# Install the CLI tool globally
dotnet tool install --global MarkItDown.Cli

# Convert a file to Markdown
markitdown input.html

# Specify output file
markitdown input.html -o output.md

# Read from stdin
echo "<h1>Hello</h1>" | markitdown

# Enable verbose logging
markitdown input.html --verbose
```

## 🏗️ Architecture

### Core Components

- **`MarkItDown`** - Main entry point for conversions
- **`IDocumentConverter`** - Interface for format-specific converters
- **`DocumentConverterResult`** - Contains the converted Markdown and optional metadata
- **`StreamInfo`** - Metadata about the input stream (MIME type, extension, charset, etc.)
- **`ConverterRegistration`** - Associates converters with priority for selection

### Built-in Converters

- **`PlainTextConverter`** - Handles text files, JSON, Markdown, etc.
- **`HtmlConverter`** - Converts HTML to Markdown using HtmlAgilityPack

### Converter Priority System

Converters are selected based on priority (lower values = higher priority):
- `ConverterPriority.SpecificFileFormat` (0.0) - For specific formats like .html, .pdf
- `ConverterPriority.GenericFileFormat` (10.0) - For generic formats like text/*

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

### Project Structure

```
├── src/
│   ├── MarkItDown.Core/           # Core library (NuGet package)
│   │   ├── Converters/            # Format-specific converters
│   │   ├── MarkItDown.cs          # Main conversion class
│   │   ├── IDocumentConverter.cs  # Converter interface
│   │   └── ...                    # Supporting classes
│   └── MarkItDown.Cli/            # Command-line tool
├── tests/
│   └── MarkItDown.Tests/          # Unit tests with xUnit
└── README.md                      # This file
```

### Contributing Guidelines

1. **Fork** the repository
2. **Create** a feature branch (`git checkout -b feature/amazing-feature`)
3. **Add tests** for your changes
4. **Ensure** all tests pass (`dotnet test`)
5. **Update** documentation if needed
6. **Submit** a pull request

## 🗺️ Roadmap

### 🎯 Version 1.1 - Enhanced Format Support
- **PDF Support** (using iText7 or PdfPig)
- **Office Documents** (.docx, .xlsx, .pptx using DocumentFormat.OpenXml)
- **Improved error handling** and diagnostics

### 🎯 Version 1.2 - Advanced Features  
- **Image OCR** (using ImageSharp + Tesseract)
- **Audio transcription** integration
- **CSV/Excel** advanced table formatting
- **Performance optimizations**

### 🎯 Version 2.0 - Enterprise Features
- **Azure Document Intelligence** integration
- **Plugin system** for external converters
- **Advanced configuration** options
- **Batch processing** capabilities

## 📈 Performance

MarkItDown is designed for high performance with:
- **Stream-based processing** - No temporary files
- **Async/await patterns** - Non-blocking I/O operations  
- **Memory efficiency** - Minimal memory footprint
- **Parallel processing** - Handle multiple documents concurrently

## 🔧 Configuration

```csharp
// Configure converters
var options = new MarkItDownOptions
{
    DefaultEncoding = Encoding.UTF8,
    MaxFileSize = 10 * 1024 * 1024, // 10MB
    Timeout = TimeSpan.FromMinutes(5)
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
