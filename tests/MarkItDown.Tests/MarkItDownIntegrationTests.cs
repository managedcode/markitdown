using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;

namespace MarkItDown.Tests;

public class MarkItDownIntegrationTests
{
    [Fact]
    public async Task ConvertAsync_WithValidFile_ReturnsSuccess()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDown();
        var htmlContent = "<html><body><h1>Test Header</h1><p>Test content</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(htmlContent);
        
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(mimeType: "text/html", extension: ".html");

        // Act
        var result = await markItDown.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Markdown);
        Assert.Contains("# Test Header", result.Markdown);
        Assert.Contains("Test content", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_WithCancellationToken_RespectsToken()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDown();
        var content = "Simple text content for testing";
        var bytes = Encoding.UTF8.GetBytes(content);
        var cts = new CancellationTokenSource();
        
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(extension: ".txt");

        // Act & Assert
        cts.Cancel();
        
        // The cancellation might be caught during the conversion process
        // Let's check that either OperationCanceledException or UnsupportedFormatException 
        // with an inner TaskCanceledException is thrown
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => markItDown.ConvertAsync(stream, streamInfo, cts.Token));
            
        // Verify that cancellation was involved somehow
        Assert.True(ex is OperationCanceledException || 
                   ex is UnsupportedFormatException ufe && 
                   ufe.InnerException is AggregateException ae && 
                   ae.InnerExceptions.Any(e => e is TaskCanceledException));
    }

    [Fact]
    public async Task ConvertAsync_WithLargeContent_ProcessesCorrectly()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDown();
        var largeContent = new StringBuilder();
        
        // Create a large HTML document
        largeContent.AppendLine("<html><body>");
        for (int i = 0; i < 1000; i++)
        {
            largeContent.AppendLine($"<h2>Section {i}</h2>");
            largeContent.AppendLine($"<p>This is content for section {i}. It contains some text to make the document larger.</p>");
        }
        largeContent.AppendLine("</body></html>");
        
        var bytes = Encoding.UTF8.GetBytes(largeContent.ToString());
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(mimeType: "text/html", extension: ".html");

        // Act
        var result = await markItDown.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Markdown);
        Assert.Contains("## Section 0", result.Markdown);
        Assert.Contains("## Section 999", result.Markdown);
        
        // Verify the content is substantial
        Assert.True(result.Markdown.Length > 10000);
    }

    [Fact]
    public async Task ConvertAsync_StreamNotSeekable_HandlesCorrectly()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDown();
        var content = "Test content for non-seekable stream";
        var bytes = Encoding.UTF8.GetBytes(content);
        
        using var nonSeekableStream = new NonSeekableMemoryStream(bytes);
        var streamInfo = new StreamInfo(extension: ".txt");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => markItDown.ConvertAsync(nonSeekableStream, streamInfo));
    }

    [Fact]
    public async Task ConvertAsync_EmptyStream_ReturnsEmptyResult()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDown();
        using var stream = new MemoryStream();
        var streamInfo = new StreamInfo(extension: ".txt");

        // Act
        var result = await markItDown.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_BinaryContent_ThrowsUnsupportedFormatException()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDown();
        var binaryData = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 }; // Random binary data
        
        using var stream = new MemoryStream(binaryData);
        var streamInfo = new StreamInfo(mimeType: "application/octet-stream", extension: ".bin");

        // Act & Assert
        await Assert.ThrowsAsync<UnsupportedFormatException>(
            () => markItDown.ConvertAsync(stream, streamInfo));
    }

    [Fact]
    public async Task ConvertAsync_JsonContent_ReturnsFormattedOutput()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDown();
        var jsonContent = "{\"name\": \"test\", \"value\": 123, \"nested\": {\"key\": \"value\"}}";
        var bytes = Encoding.UTF8.GetBytes(jsonContent);
        
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(mimeType: "application/json", extension: ".json");

        // Act
        var result = await markItDown.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Markdown);
        Assert.Contains("```json", result.Markdown);
        Assert.Contains("\"name\": \"test\"", result.Markdown);
        Assert.Contains("\"value\": 123", result.Markdown);
        Assert.Contains("\"key\": \"value\"", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_MarkdownContent_ReturnsAsIs()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDown();
        var markdownContent = "# Header\n\nThis is **bold** and *italic* text.\n\n- List item 1\n- List item 2\n";
        var bytes = Encoding.UTF8.GetBytes(markdownContent);
        
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(mimeType: "text/markdown", extension: ".md");

        // Act
        var result = await markItDown.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(markdownContent, result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_ComplexHtmlWithTables_ConvertsCorrectly()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDown();
        var htmlContent = @"
            <html>
                <body>
                    <h1>Test Document</h1>
                    <table>
                        <thead>
                            <tr><th>Name</th><th>Age</th><th>City</th></tr>
                        </thead>
                        <tbody>
                            <tr><td>John</td><td>30</td><td>New York</td></tr>
                            <tr><td>Jane</td><td>25</td><td>Boston</td></tr>
                        </tbody>
                    </table>
                    <blockquote>
                        <p>This is a quote with <strong>bold</strong> text.</p>
                    </blockquote>
                    <pre><code>console.log('Hello World');</code></pre>
                </body>
            </html>";
        var bytes = Encoding.UTF8.GetBytes(htmlContent);
        
        using var stream = new MemoryStream(bytes);
        var streamInfo = new StreamInfo(mimeType: "text/html", extension: ".html");

        // Act
        var result = await markItDown.ConvertAsync(stream, streamInfo);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("# Test Document", result.Markdown);
        Assert.Contains("| Name | Age | City |", result.Markdown);
        Assert.Contains("| --- | --- | --- |", result.Markdown);
        Assert.Contains("| John | 30 | New York |", result.Markdown);
        Assert.Contains("> This is a quote", result.Markdown);
        Assert.Contains("```", result.Markdown);
        Assert.Contains("console.log", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_MultipleConcurrentCalls_HandlesCorrectly()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDown();
        var tasks = new Task<DocumentConverterResult>[10];
        
        for (int i = 0; i < 10; i++)
        {
            var content = $"<h1>Document {i}</h1><p>Content for document {i}</p>";
            var bytes = Encoding.UTF8.GetBytes(content);
            var stream = new MemoryStream(bytes);
            var streamInfo = new StreamInfo(mimeType: "text/html", extension: ".html");
            
            tasks[i] = markItDown.ConvertAsync(stream, streamInfo);
        }

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        for (int i = 0; i < 10; i++)
        {
            Assert.NotNull(results[i]);
            Assert.Contains($"# Document {i}", results[i].Markdown);
            Assert.Contains($"Content for document {i}", results[i].Markdown);
        }
    }

    [Fact]
    public void RegisterConverter_CustomConverter_AddsToList()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDown();
        var customConverter = new TestCustomConverter();

        // Act
        markItDown.RegisterConverter(customConverter);

        // Assert - The converter should be registered
        // We can't directly test the internal list, but we can test that it's used
        var converters = markItDown.GetRegisteredConverters();
        Assert.Contains(converters, c => c.GetType() == typeof(TestCustomConverter));
    }

    [Fact]
    public void RegisterConverter_NullConverter_ThrowsArgumentNullException()
    {
        // Arrange
        var markItDown = new global::MarkItDown.MarkItDown();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => markItDown.RegisterConverter(null!));
    }

    // Helper class for testing non-seekable streams
    private class NonSeekableMemoryStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableMemoryStream(byte[] data)
        {
            _inner = new MemoryStream(data);
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false; // This is the key difference
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        
        public override long Position 
        { 
            get => _inner.Position; 
            set => throw new NotSupportedException("Stream is not seekable");
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("Stream is not seekable");
        public override void SetLength(long value) => throw new NotSupportedException("Stream is not seekable");
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner?.Dispose();
            base.Dispose(disposing);
        }
    }

    // Helper class for testing custom converters
    private class TestCustomConverter : IDocumentConverter
    {
        public int Priority => 50;

        public bool AcceptsInput(StreamInfo streamInfo)
        {
            return streamInfo.Extension == ".test";
        }

        public Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        {
            var result = new DocumentConverterResult("# Test Custom Converter\n\nThis is from the test converter.");
            return Task.FromResult(result);
        }
    }
}
