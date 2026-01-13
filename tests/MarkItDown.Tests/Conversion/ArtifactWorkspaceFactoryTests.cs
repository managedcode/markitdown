using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ManagedCode.MimeTypes;
using ManagedCode.Storage.Azure;
using ManagedCode.Storage.Azure.Options;
using MarkItDown;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Testcontainers.Azurite;
using Xunit;
using Xunit.Sdk;

namespace MarkItDown.Tests.Conversion;

public sealed class ArtifactWorkspaceFactoryTests
{
    [Fact]
    public void CreateWorkspace_DefaultOptions_DeletesDirectoryOnDispose()
    {
        var streamInfo = new StreamInfo(fileName: "sample.pdf");
        var segmentOptions = SegmentOptions.Default;
        var storageOptions = ArtifactStorageOptions.Default;

        var workspace = ArtifactWorkspaceFactory.CreateWorkspace(streamInfo, segmentOptions, storageOptions);
        var directory = workspace.DirectoryPath;

        workspace.PersistText("document.md", "content");

        Directory.Exists(directory).ShouldBeTrue();

        workspace.Dispose();

        Directory.Exists(directory).ShouldBeFalse();
    }

    [Fact]
    public void CreateWorkspace_WithKeepArtifactDirectory_PreservesDirectory()
    {
        var streamInfo = new StreamInfo(fileName: "sample.pdf");
        var segmentOptions = SegmentOptions.Default with
        {
            Image = SegmentOptions.Default.Image with
            {
                KeepArtifactDirectory = true
            }
        };
        var storageOptions = ArtifactStorageOptions.Default;

        var workspace = ArtifactWorkspaceFactory.CreateWorkspace(streamInfo, segmentOptions, storageOptions);
        var directory = workspace.DirectoryPath;

        workspace.PersistText("document.md", "content");

        Directory.Exists(directory).ShouldBeTrue();

        workspace.Dispose();

        Directory.Exists(directory).ShouldBeTrue();

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

public sealed class ArtifactWorkspaceFactoryAzureTests : IAsyncLifetime
{
    private const string AzuriteImage = "mcr.microsoft.com/azure-storage/azurite:3.28.0";
    private readonly AzuriteContainer azurite;

    public ArtifactWorkspaceFactoryAzureTests()
    {
        azurite = new AzuriteBuilder(AzuriteImage).Build();
    }

    public async Task InitializeAsync()
    {
        try
        {
            await azurite.StartAsync();
        }
        catch (Exception ex)
        {
            throw SkipException.ForSkip($"Azurite container unavailable: {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        await azurite.DisposeAsync();
    }

    [Fact]
    public async Task CreateWorkspace_WithAzureStorage_UploadsAndCleansUp()
    {
        var connectionString = azurite.GetConnectionString();
        var containerName = $"markitdown{Guid.NewGuid():N}";

        var storageOptions = ArtifactStorageOptions.Default with
        {
            StorageFactory = () => CreateAzureStorage(connectionString, containerName),
            DeleteOnDispose = true,
            DisposeStorage = true
        };

        var streamInfo = new StreamInfo(fileName: "sample.pdf");

        var workspace = ArtifactWorkspaceFactory.CreateWorkspace(streamInfo, SegmentOptions.Default, storageOptions);
        var disposed = false;

        try
        {
            workspace.IsStorageBacked.ShouldBeTrue();
            workspace.DirectoryPath.ShouldNotBeNull();
            Directory.Exists(workspace.DirectoryPath).ShouldBeTrue();

            var markdownPath = workspace.PersistText("doc.md", "hello");
            markdownPath.ShouldNotBeNull();
            var storageDirectory = workspace.StorageDirectory;
            storageDirectory.ShouldNotBeNull();

            await workspace.DisposeAsync();
            disposed = true;

            Directory.Exists(workspace.DirectoryPath).ShouldBeFalse();

            var clientOptions = new BlobClientOptions(BlobClientOptions.ServiceVersion.V2023_11_03);
            var blobClient = new BlobContainerClient(connectionString, containerName, clientOptions);
            var remaining = 0;
            await foreach (var _ in blobClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, storageDirectory, CancellationToken.None))
            {
                remaining++;
            }

            remaining.ShouldBe(0);

            await blobClient.DeleteIfExistsAsync();
        }
        finally
        {
            if (!disposed)
            {
                await workspace.DisposeAsync();
            }
        }
    }

    private static AzureStorage CreateAzureStorage(string connectionString, string container)
    {
        var options = new AzureStorageOptions
        {
            ConnectionString = connectionString,
            Container = container,
            CreateContainerIfNotExists = true,
            OriginalOptions = new BlobClientOptions(BlobClientOptions.ServiceVersion.V2023_11_03)
        };

        return new AzureStorage(options, NullLogger<AzureStorage>.Instance);
    }

    [Fact]
    public async Task ConvertAsync_WithAzureStorage_PersistsArtifacts()
    {
        var connectionString = azurite.GetConnectionString();
        var containerName = $"markitdown{Guid.NewGuid():N}";

        var storageOptions = ArtifactStorageOptions.Default with
        {
            StorageFactory = () => CreateAzureStorage(connectionString, containerName),
            DeleteOnDispose = false,
            DisposeStorage = true,
            PersistMarkdown = true,
            CopySourceDocument = true
        };

        var options = new MarkItDownOptions
        {
            ArtifactStorage = storageOptions
        };

        var inputPath = TestAssetLoader.GetAssetPath(TestAssetCatalog.AutogenPaperDocx);

        var client = new MarkItDownClient(options);
        await using var result = await client.ConvertAsync(inputPath);

        var blobOptions = new BlobClientOptions(BlobClientOptions.ServiceVersion.V2023_11_03);
        var containerClient = new BlobContainerClient(connectionString, containerName, blobOptions);

        var blobNames = new List<string>();
        await foreach (var blob in containerClient.GetBlobsAsync())
        {
            blobNames.Add(blob.Name);
        }

        blobNames.ShouldNotBeEmpty();
        blobNames.ShouldContain(name => name.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        blobNames.ShouldContain(name => name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase));

        await containerClient.DeleteIfExistsAsync();
    }
}
