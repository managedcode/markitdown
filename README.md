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
| **Plain Text** | `.txt`, `.md`, `.json` | ✅ Supported | Direct text processing |
| **PDF** | `.pdf` | 🚧 Planned | Adobe PDF documents |
| **Word** | `.docx` | 🚧 Planned | Microsoft Word documents |
| **Excel** | `.xlsx` | 🚧 Planned | Microsoft Excel spreadsheets |
| **PowerPoint** | `.pptx` | 🚧 Planned | Microsoft PowerPoint presentations |
| **Images** | `.jpg`, `.png`, `.gif` | 🚧 Planned | OCR-based text extraction |

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
