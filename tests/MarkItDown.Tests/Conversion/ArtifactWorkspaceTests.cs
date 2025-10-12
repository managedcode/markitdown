using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;
using Xunit;

namespace MarkItDown.Tests.Conversion;

public class ArtifactWorkspaceTests
{
    [Fact]
    public async Task PersistBinary_WritesFileAndCleansUp()
    {
        var root = CreateTempDirectory();
        await using (var workspace = ArtifactWorkspace.Create(root, deleteOnDispose: true))
        {
            var path = workspace.PersistBinary("payload.bin", new byte[] { 1, 2, 3 }, mimeType: null, CancellationToken.None);
            Assert.True(File.Exists(path));
        }

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task PersistText_WritesContent()
    {
        var root = CreateTempDirectory();
        await using var workspace = ArtifactWorkspace.Create(root, deleteOnDispose: true);

        var path = workspace.PersistText("note.md", "hello", mimeType: null, CancellationToken.None);
        Assert.True(File.Exists(path));
        Assert.Equal("hello", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task PersistFile_CopiesSource()
    {
        var root = CreateTempDirectory();
        var source = Path.Combine(root, "source.txt");
        await File.WriteAllTextAsync(source, "content");

        await using var workspace = ArtifactWorkspace.Create(root, deleteOnDispose: true);
        var target = workspace.PersistFile("copy.txt", source, mimeType: null, CancellationToken.None);

        Assert.True(File.Exists(target));
        Assert.Equal("content", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task WorkspacePersistence_CopiesSourceAndMarkdown()
    {
        var root = CreateTempDirectory();
        var source = Path.Combine(root, "document.txt");
        await File.WriteAllTextAsync(source, "workspace test");

        var streamInfo = new StreamInfo(fileName: "document.txt", extension: ".txt");
        var options = ArtifactStorageOptions.Default with { DeleteOnDispose = true, PersistMarkdown = true, CopySourceDocument = true };

        string? persistedSource;
        await using (var workspace = WorkspacePersistence.CreateWorkspace(streamInfo, SegmentOptions.Default, options, source, ".txt", "text/plain", CancellationToken.None, out persistedSource))
        {
            Assert.NotNull(persistedSource);
            Assert.True(File.Exists(persistedSource));

            var markdownPath = WorkspacePersistence.PersistMarkdown(workspace, options, streamInfo, "# heading", CancellationToken.None);
            Assert.NotNull(markdownPath);
            Assert.True(File.Exists(markdownPath));
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "markitdown-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }
}
