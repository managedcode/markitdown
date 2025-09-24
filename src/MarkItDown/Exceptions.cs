namespace MarkItDown;

/// <summary>
/// Base exception for MarkItDown operations.
/// </summary>
public class MarkItDownException : Exception
{
    public MarkItDownException() { }
    public MarkItDownException(string message) : base(message) { }
    public MarkItDownException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a required dependency is missing.
/// </summary>
public class MissingDependencyException : MarkItDownException
{
    public MissingDependencyException() { }
    public MissingDependencyException(string message) : base(message) { }
    public MissingDependencyException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a file conversion fails.
/// </summary>
public class FileConversionException : MarkItDownException
{
    public FileConversionException() { }
    public FileConversionException(string message) : base(message) { }
    public FileConversionException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when an unsupported format is encountered.
/// </summary>
public class UnsupportedFormatException : MarkItDownException
{
    public UnsupportedFormatException() { }
    public UnsupportedFormatException(string message) : base(message) { }
    public UnsupportedFormatException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a conversion attempt fails.
/// </summary>
public class FailedConversionAttemptException : MarkItDownException
{
    public FailedConversionAttemptException() { }
    public FailedConversionAttemptException(string message) : base(message) { }
    public FailedConversionAttemptException(string message, Exception innerException) : base(message, innerException) { }
}