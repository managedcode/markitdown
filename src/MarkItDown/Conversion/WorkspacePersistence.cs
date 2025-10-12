using System.IO;
using System.Threading;
using ManagedCode.MimeTypes;

namespace MarkItDown;

internal static class WorkspacePersistence
{
    public static ArtifactWorkspace CreateWorkspace(
        StreamInfo streamInfo,
        SegmentOptions segmentOptions,
        ArtifactStorageOptions storageOptions,
        string sourcePath,
        string defaultExtension,
        string defaultMimeType,
        CancellationToken cancellationToken,
        out string? storedSourcePath)
    {
        var workspace = ArtifactWorkspaceFactory.CreateWorkspace(streamInfo, segmentOptions, storageOptions);
        storedSourcePath = null;

        if (storageOptions.CopySourceDocument)
        {
            var extension = Path.GetExtension(streamInfo.FileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = Path.GetExtension(sourcePath);
            }

            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = defaultExtension;
            }

            var fileName = FileNameSanitizer.BuildFileName(streamInfo.FileName, "source", extension);
            var mime = streamInfo.ResolveMimeType() ?? defaultMimeType;
            storedSourcePath = workspace.PersistFile(fileName, sourcePath, mime, cancellationToken);
        }

        return workspace;
    }

    public static string? PersistMarkdown(
        ArtifactWorkspace workspace,
        ArtifactStorageOptions storageOptions,
        StreamInfo streamInfo,
        string markdown,
        CancellationToken cancellationToken)
    {
        if (!storageOptions.PersistMarkdown)
        {
            return null;
        }

        var markdownFileName = FileNameSanitizer.BuildFileName(streamInfo.FileName, "document", ".md");
        return workspace.PersistText(markdownFileName, markdown, MimeHelper.MARKDOWN, cancellationToken);
    }
}
