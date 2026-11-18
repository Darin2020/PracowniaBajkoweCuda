namespace BajkoweCuda.Domain.Chat;

/// <summary>
/// An uploaded image (docs/PLAN.md §5, §8.4). It is uploaded first and becomes part of a message when the message is
/// sent; images that never get sent are deleted after a day. The blobs hold the server's re-encoded copies, never the
/// uploaded file itself, under random names.
/// </summary>
public sealed class Attachment
{
    public const int ContainerMaxLength = 63;
    public const int BlobNameMaxLength = 128;
    public const int ContentTypeMaxLength = 32;

    /// <summary>How long an upload may wait for its message before it is cleaned up.</summary>
    public static readonly TimeSpan UnsentLifetime = TimeSpan.FromHours(24);

    private Attachment()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Who uploaded it (an <c>ApplicationUser</c> id). Only they can send it.</summary>
    public Guid OwnerUserId { get; private set; }

    /// <summary>The message it belongs to; <c>null</c> until it is sent.</summary>
    public Guid? MessageId { get; private set; }

    public string Container { get; private set; } = string.Empty;

    /// <summary>The display size (at most 1600 px).</summary>
    public string BlobName { get; private set; } = string.Empty;

    /// <summary>The thumbnail (at most 480 px).</summary>
    public string ThumbnailBlobName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    /// <summary>Size of the display copy in bytes.</summary>
    public long SizeBytes { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Attachment Uploaded(
        Guid id,
        Guid ownerUserId,
        string container,
        string blobName,
        string thumbnailBlobName,
        string contentType,
        long sizeBytes,
        int width,
        int height,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailBlobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sizeBytes, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);

        return new Attachment
        {
            Id = id,
            OwnerUserId = ownerUserId,
            Container = container,
            BlobName = blobName,
            ThumbnailBlobName = thumbnailBlobName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Width = width,
            Height = height,
            CreatedAt = now,
        };
    }

    /// <summary>Whether the cleanup may delete it: never sent, and older than <see cref="UnsentLifetime"/>.</summary>
    public bool IsAbandoned(DateTimeOffset now) => MessageId is null && now - CreatedAt >= UnsentLifetime;

    internal void AttachTo(Message message, Guid senderId)
    {
        if (MessageId is not null || OwnerUserId != senderId)
        {
            throw new DomainException(ChatErrors.AttachmentUnavailable, "The image is not available for this message.");
        }

        MessageId = message.Id;
    }
}
