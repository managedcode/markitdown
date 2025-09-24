using System.Collections.Generic;

namespace MarkItDown.Tests;

public sealed record FileTestVector(
    string FileName,
    string? MimeType,
    string? Charset,
    string? Url,
    IReadOnlyList<string> MustInclude,
    IReadOnlyList<string> MustNotInclude,
    bool SupportsStreamGuess = true,
    bool SupportsDataUri = true
);
