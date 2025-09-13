# GitHub Copilot Instructions for MarkItDown C# .NET Project

## Project Overview

MarkItDown is a C# .NET 8 library for converting various document formats (HTML, PDF, DOCX, XLSX, etc.) into clean Markdown suitable for Large Language Models (LLMs) and text analysis pipelines. This project is a conversion from the original Python implementation to C# while maintaining API compatibility and adding modern async/await patterns.

## Architecture and Design Principles

### Core Components

```
src/
├── MarkItDown.Core/           # Main library project
│   ├── IDocumentConverter.cs  # Converter interface
│   ├── MarkItDown.cs         # Main orchestration class
│   ├── StreamInfo.cs         # File metadata handling
│   ├── DocumentConverterResult.cs # Conversion results
│   ├── Exceptions/           # Exception hierarchy
│   └── Converters/           # Format-specific converters
├── MarkItDown.Cli/           # Command line tool
tests/
└── MarkItDown.Tests/         # Unit tests with xUnit
```

### Key Design Patterns

1. **Interface-Based Architecture**: All converters implement `IDocumentConverter`
2. **Async/Await Throughout**: Modern C# async patterns for I/O operations
3. **Priority-Based Registration**: Converters are ordered by priority for format detection
4. **Stream-Based Processing**: Avoid temporary files, work with streams
5. **Comprehensive Error Handling**: Specific exception types for different failure modes

## Code Quality Standards

### C# Coding Conventions

- **Target Framework**: .NET 8.0 (net8.0)
- **Language Version**: C# 12
- **Nullable Reference Types**: Enabled
- **Async Patterns**: Use async/await, ConfigureAwait(false) for library code
- **Exception Handling**: Specific exception types, never swallow exceptions

### Naming Conventions

- **Classes**: PascalCase (`DocumentConverter`, `StreamInfo`)
- **Methods**: PascalCase (`ConvertAsync`, `AcceptsInput`)
- **Properties**: PascalCase (`Markdown`, `MimeType`)
- **Fields**: _camelCase with underscore prefix (`_logger`, _converters`)
- **Constants**: PascalCase (`DefaultPriority`)
- **Interfaces**: IPascalCase (`IDocumentConverter`)

### Method Signatures

```csharp
// Async methods should always return Task<T> or Task
public async Task<DocumentConverterResult> ConvertAsync(
    Stream stream, 
    StreamInfo streamInfo, 
    CancellationToken cancellationToken = default)

// Interface implementations should be explicit about async
bool AcceptsInput(StreamInfo streamInfo);
```

### Error Handling Patterns

```csharp
// Custom exceptions for specific failure modes
public class UnsupportedFormatException : MarkItDownException
{
    public UnsupportedFormatException(string format) 
        : base($"Unsupported format: {format}") { }
}

// Proper async exception handling
try
{
    var result = await converter.ConvertAsync(stream, info, cancellationToken);
    return result;
}
catch (UnsupportedFormatException)
{
    throw; // Re-throw specific exceptions
}
catch (Exception ex)
{
    throw new MarkItDownException("Conversion failed", ex);
}
```

### Testing Standards

- **Framework**: xUnit with standard assertions
- **Async Testing**: Proper async test methods
- **Test Naming**: `MethodName_Scenario_ExpectedResult`
- **Coverage**: All public APIs must have tests
- **Edge Cases**: Test null inputs, empty streams, invalid data

```csharp
[Fact]
public async Task ConvertAsync_ValidHtml_ReturnsCorrectMarkdown()
{
    // Arrange
    var converter = new HtmlConverter();
    var html = "<h1>Test</h1><p>Content</p>";
    var bytes = Encoding.UTF8.GetBytes(html);
    using var stream = new MemoryStream(bytes);
    var streamInfo = new StreamInfo(mimeType: "text/html");

    // Act
    var result = await converter.ConvertAsync(stream, streamInfo);

    // Assert
    Assert.Contains("# Test", result.Markdown);
    Assert.Contains("Content", result.Markdown);
}
```

## Converter Implementation Guidelines

### Creating New Converters

1. **Inherit from Base**: Consider if a base converter class would help
2. **Implement Interface**: All converters must implement `IDocumentConverter`
3. **Priority Assignment**: Lower numbers = higher priority (HTML = 100, Plain Text = 1000)
4. **Format Detection**: Be specific in `AcceptsInput` - check MIME type AND extension
5. **Error Handling**: Wrap third-party exceptions in `MarkItDownException`

### Standard Converter Structure

```csharp
public class YourFormatConverter : IDocumentConverter
{
    public int Priority => 200; // Between HTML(100) and PlainText(1000)

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        return streamInfo.MimeType?.StartsWith("application/your-format") == true ||
               streamInfo.Extension?.ToLowerInvariant() == ".your-ext";
    }

    public async Task<DocumentConverterResult> ConvertAsync(
        Stream stream, 
        StreamInfo streamInfo, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Reset stream position
            if (stream.CanSeek)
                stream.Position = 0;

            // Your conversion logic here
            var markdown = await ConvertToMarkdownAsync(stream, cancellationToken);
            
            return new DocumentConverterResult(
                markdown: markdown,
                title: ExtractTitle(markdown) // Optional
            );
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
        {
            throw new MarkItDownException($"Failed to convert {streamInfo.Extension} file", ex);
        }
    }
}
```

## Package Management and Dependencies

### NuGet Package References

- **Core Dependencies**: Keep minimal - only what's absolutely needed
- **Version Pinning**: Use specific versions for reproducible builds
- **License Compatibility**: Ensure all dependencies are MIT-compatible
- **Security**: Regularly update packages for security fixes

### Current Key Dependencies

```xml
<PackageReference Include="HtmlAgilityPack" Version="1.11.71" />
<PackageReference Include="System.Text.Json" Version="8.0.5" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.1" />
```

## Testing Philosophy

### Test Coverage Requirements

- **Every Public Method**: Must have at least basic functionality tests
- **Error Conditions**: Test exception scenarios and edge cases
- **Integration Tests**: Test the full MarkItDown workflow
- **Format-Specific Tests**: Each converter needs comprehensive tests

### Test Data Strategy

```csharp
// Use test data that mirrors the original Python test vectors
public static class TestVectors
{
    public static readonly FileTestVector[] GeneralTestVectors = {
        new FileTestVector(
            filename: "test.html",
            mimeType: "text/html",
            mustInclude: new[] { "# Header", "**bold text**" },
            mustNotInclude: new[] { "<html>", "<script>" }
        )
    };
}
```

### Performance Testing Considerations

- **Large Files**: Test with files >1MB
- **Memory Usage**: Ensure streaming doesn't load entire files into memory
- **Async Patterns**: Verify proper async/await usage with real I/O

## CLI Tool Guidelines

### Command Line Interface

- **System.CommandLine**: Use modern .NET CLI framework
- **Error Codes**: Return appropriate exit codes (0 = success, 1 = error)
- **Logging**: Support verbose output for debugging
- **File Handling**: Support both file paths and stdin/stdout

### CLI Error Handling

```csharp
try
{
    var result = await markItDown.ConvertAsync(inputStream, streamInfo);
    await Console.Out.WriteAsync(result.Markdown);
    return 0;
}
catch (UnsupportedFormatException ex)
{
    await Console.Error.WriteLineAsync($"Error: {ex.Message}");
    return 1;
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"Unexpected error: {ex.Message}");
    return 2;
}
```

## Future Extension Points

### Adding New Format Support

Priority for new converters:
1. **PDF Support** (iText7 or PdfPig)
2. **Office Documents** (DocumentFormat.OpenXml)
3. **Images with OCR** (ImageSharp + Tesseract)
4. **Audio Transcription** (Azure Speech Services)
5. **CSV/Excel** (EPPlus or ClosedXML)

### Converter Development Workflow

1. **Research Python Implementation**: Understand the original converter
2. **Choose .NET Library**: Find appropriate NuGet packages
3. **Create Test Cases**: Port Python test vectors to C#
4. **Implement Converter**: Follow the patterns above
5. **Integration Testing**: Test with MarkItDown main class
6. **Documentation**: Update README with new format support

## Maintenance and Updates

### Version Compatibility

- **Semantic Versioning**: Follow SemVer for releases
- **API Stability**: Don't break public interfaces without major version bump
- **Backward Compatibility**: Maintain compatibility with existing code

### Documentation Requirements

- **XML Comments**: All public APIs need XML documentation
- **README Updates**: Keep feature matrix current
- **API Examples**: Provide working code examples
- **Migration Guides**: Help users migrate from Python version

## Build and Deployment

### Project Configuration

```xml
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <Nullable>enable</Nullable>
  <LangVersion>12</LangVersion>
  <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
</PropertyGroup>
```

### NuGet Package Metadata

- **PackageId**: MarkItDown
- **Authors**: ManagedCode
- **Description**: Clear, concise description
- **Tags**: Include relevant keywords for discovery
- **License**: MIT
- **Repository URL**: GitHub repository link

## Development Best Practices

### Code Reviews

- **Interface Design**: Review public APIs carefully
- **Performance**: Check for memory leaks and performance issues
- **Error Handling**: Ensure proper exception handling
- **Tests**: Verify comprehensive test coverage
- **Documentation**: Check XML comments and README updates

### Debugging Guidelines

```csharp
// Use structured logging for debugging
_logger.LogDebug("Converting {FileName} with MIME type {MimeType}", 
    streamInfo.FileName, streamInfo.MimeType);

// Add timing for performance analysis
using var activity = MarkItDownActivity.StartActivity("ConvertDocument");
activity?.SetTag("format", streamInfo.Extension);
```

This document should guide all development work on the MarkItDown C# project, ensuring consistency, quality, and maintainability as the project grows.