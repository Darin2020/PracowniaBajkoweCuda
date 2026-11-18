namespace BajkoweCuda.Application.Files;

/// <summary>
/// Private file storage (Azure Blob Storage, Azurite locally; docs/PLAN.md §8.4). Files are never public: the API
/// streams them after an access check (<c>/api/files/{id}</c>).
/// </summary>
public interface IFileStore
{
    /// <summary>Stores a file under the given name, replacing nothing: names are random and new.</summary>
    Task SaveAsync(string container, string name, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken);

    /// <summary>Opens a file for reading, or returns <c>null</c> when it does not exist.</summary>
    Task<Stream?> OpenReadAsync(string container, string name, CancellationToken cancellationToken);

    /// <summary>Deletes a file; a missing file is fine.</summary>
    Task DeleteAsync(string container, string name, CancellationToken cancellationToken);
}

/// <summary>Container names (created by infra/modules/storage.bicep in Azure, on first use locally).</summary>
public static class FileContainers
{
    public const string Attachments = "attachments";
    public const string OrderPhotos = "order-photos";
}
