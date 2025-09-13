# MarkItDown - C# .NET 9 Version

[![.NET](https://img.shields.io/badge/.NET-9.0-blue)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

> This is a C# .NET 9 conversion of the original [Microsoft MarkItDown](https://github.com/microsoft/markitdown) Python project.

MarkItDown is a C# library for converting various file formats to Markdown for use with LLMs and text analysis pipelines. This library focuses on preserving important document structure and content as Markdown (including: headings, lists, tables, links, etc.) while providing a clean, async API.

## Current Format Support

- **Plain Text** (.txt, .md, .json, etc.)
- **HTML** (.html, .htm) - with support for:
  - Headers (H1-H6) → Markdown headers
  - Bold/Strong text → **bold**
  - Italic/Emphasis text → *italic*
  - Links → [text](url)
  - Images → ![alt](src)
  - Lists (ordered/unordered)
  - Tables with header detection
  - Code blocks and inline code
  - Blockquotes

## Installation

### Prerequisites
- .NET 9.0 SDK or later

### Building from Source
```bash
git clone https://github.com/managedcode/markitdown.git
cd markitdown
dotnet build
```

### Running Tests
```bash
dotnet test
```

## Usage

### Command Line Interface

```bash
# Convert a file to Markdown
dotnet run --project src/MarkItDown.Cli -- input.html

# Specify output file
dotnet run --project src/MarkItDown.Cli -- input.html -o output.md

# Read from stdin
echo "<h1>Hello</h1>" | dotnet run --project src/MarkItDown.Cli

# Enable verbose logging
dotnet run --project src/MarkItDown.Cli -- input.html --verbose
```

### C# API

```csharp
using MarkItDown.Core;
using Microsoft.Extensions.Logging;

// Basic usage
var markItDown = new MarkItDown();
var result = await markItDown.ConvertAsync("document.html");
Console.WriteLine(result.Markdown);

// With logging and HTTP client for web content
using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var logger = loggerFactory.CreateLogger<Program>();

using var httpClient = new HttpClient();
var markItDownWithOptions = new MarkItDown(logger, httpClient);

// Convert from file
var fileResult = await markItDownWithOptions.ConvertAsync("document.html");

// Convert from URL
var urlResult = await markItDownWithOptions.ConvertFromUrlAsync("https://example.com");

// Convert from stream
using var stream = File.OpenRead("document.html");
var streamInfo = new StreamInfo(mimeType: "text/html", extension: ".html");
var streamResult = await markItDownWithOptions.ConvertAsync(stream, streamInfo);
```

### Custom Converters

```csharp
using MarkItDown.Core;

// Implement a custom converter
public class MyCustomConverter : IDocumentConverter
{
    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        return streamInfo.Extension == ".mycustomformat";
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
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

## Architecture

### Core Components

- **`MarkItDown`** - Main entry point for conversions
- **`IDocumentConverter`** - Interface for format-specific converters
- **`DocumentConverterResult`** - Contains the converted Markdown and optional metadata
- **`StreamInfo`** - Metadata about the input stream (MIME type, extension, charset, etc.)
- **`ConverterRegistration`** - Associates converters with priority for selection

### Built-in Converters

- **`PlainTextConverter`** - Handles text files, JSON, Markdown, etc.
- **`HtmlConverter`** - Converts HTML to Markdown using HtmlAgilityPack

### Converter Priority

Converters are selected based on priority (lower values = higher priority):
- `ConverterPriority.SpecificFileFormat` (0.0) - For specific formats like .html, .pdf
- `ConverterPriority.GenericFileFormat` (10.0) - For generic formats like text/*

## Project Structure

```
├── src/
│   ├── MarkItDown.Core/           # Core library
│   │   ├── Converters/            # Format-specific converters
│   │   ├── MarkItDown.cs          # Main conversion class
│   │   ├── IDocumentConverter.cs  # Converter interface
│   │   └── ...                    # Supporting classes
│   └── MarkItDown.Cli/            # Command-line interface
├── tests/
│   └── MarkItDown.Tests/          # Unit tests
└── original-project/              # Original Python implementation
```

## Roadmap

### Planned Converters
- **PDF** (using iText7 or PdfPig)
- **Word Documents** (.docx using DocumentFormat.OpenXml)
- **Excel Spreadsheets** (.xlsx using ClosedXML)
- **PowerPoint** (.pptx)
- **Images** (with OCR using ImageSharp + Tesseract)
- **Audio** (with transcription)
- **CSV** (with table formatting)
- **XML** (with structure preservation)
- **ZIP** (recursive processing)

### Planned Features
- NuGet package distribution
- Azure Document Intelligence integration
- Plugin system for external converters
- Advanced configuration options
- Better error handling and diagnostics

## Contributing

1. Fork the repository
2. Create a feature branch
3. Add tests for your changes
4. Ensure all tests pass (`dotnet test`)
5. Submit a pull request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

This project is a C# conversion of the original [Microsoft MarkItDown](https://github.com/microsoft/markitdown) Python library. The original project was created by the Microsoft AutoGen team.
