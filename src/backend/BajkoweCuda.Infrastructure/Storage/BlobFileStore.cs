using System.Collections.Concurrent;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BajkoweCuda.Application.Files;
using Microsoft.Extensions.Options;

namespace BajkoweCuda.Infrastructure.Storage;

/// <summary>Storage settings (docs/PLAN.md §14): the account URI in Azure, a connection string for Azurite locally.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Azure: <c>https://{account}.blob.core.windows.net/</c>, reached with the managed identity.</summary>
    public string? BlobServiceUri { get; set; }

    /// <summary>Local and tests: Azurite (<c>UseDevelopmentStorage=true</c> or the emulator's full string).</summary>
    public string? ConnectionString { get; set; }
}

/// <summary>
/// <see cref="IFileStore"/> on Azure Blob Storage. The client is created on first use, so the application starts
/// (and serves the public site) even where storage is not configured; <c>/health/ready</c> reports it.
/// With a connection string (Azurite) the containers are created on first use; in Azure the infrastructure creates
/// them and the app's identity may not.
/// </summary>
internal sealed class BlobFileStore(IOptions<StorageOptions> options) : IFileStore
{
    private readonly Lazy<BlobServiceClient> _client = new(() => CreateClient(options.Value));
    private readonly ConcurrentDictionary<string, Lazy<Task<BlobContainerClient>>> _containers = new(StringComparer.Ordinal);

    public async Task SaveAsync(string container, string name, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
    {
        var blob = (await ContainerAsync(container, cancellationToken)).GetBlobClient(name);
        await blob.UploadAsync(
            BinaryData.FromBytes(content),
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                // Names are new GUIDs; never overwrite an existing blob.
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
            },
            cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string container, string name, CancellationToken cancellationToken)
    {
        var blob = (await ContainerAsync(container, cancellationToken)).GetBlobClient(name);
        try
        {
            return await blob.OpenReadAsync(new BlobOpenReadOptions(allowModifications: false), cancellationToken);
        }
        catch (RequestFailedException error) when (error.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string container, string name, CancellationToken cancellationToken)
    {
        var blob = (await ContainerAsync(container, cancellationToken)).GetBlobClient(name);
        await blob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
    }

    /// <summary>For the readiness check: reaches the account and the attachments container.</summary>
    internal async Task CheckAsync(CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(FileContainers.Attachments, cancellationToken);
        await container.GetPropertiesAsync(cancellationToken: cancellationToken);
    }

    private async Task<BlobContainerClient> ContainerAsync(string name, CancellationToken cancellationToken)
    {
        // Shared by all callers, so not tied to one request's cancellation; a failure is not cached.
        var lazy = _containers.GetOrAdd(name, key => new Lazy<Task<BlobContainerClient>>(() => CreateContainerAsync(key, CancellationToken.None)));
        try
        {
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        catch (Exception) when (lazy.Value.IsFaulted)
        {
            _containers.TryRemove(new KeyValuePair<string, Lazy<Task<BlobContainerClient>>>(name, lazy));
            throw;
        }
    }

    private async Task<BlobContainerClient> CreateContainerAsync(string name, CancellationToken cancellationToken)
    {
        var container = _client.Value.GetBlobContainerClient(name);
        if (!string.IsNullOrWhiteSpace(options.Value.ConnectionString))
        {
            await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        }

        return container;
    }

    private static BlobServiceClient CreateClient(StorageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new BlobServiceClient(options.ConnectionString);
        }

        if (!string.IsNullOrWhiteSpace(options.BlobServiceUri))
        {
            return new BlobServiceClient(new Uri(options.BlobServiceUri), new DefaultAzureCredential());
        }

        throw new InvalidOperationException("File storage is not configured: set Storage:BlobServiceUri or Storage:ConnectionString.");
    }
}
