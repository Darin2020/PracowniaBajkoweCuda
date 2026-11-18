using BajkoweCuda.Application.Files;
using BajkoweCuda.Domain.Chat;
using BajkoweCuda.Infrastructure.Imaging;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BajkoweCuda.Infrastructure.Chat;

/// <summary>A stored file the current user may read: where it is and what it is.</summary>
public sealed record ReadableFile(string Container, string BlobName, string ContentType);

/// <summary>
/// Uploading chat images and deciding who may read them (docs/PLAN.md §3.4, §8.4). An upload is re-encoded, stored
/// under two random blob names and recorded as an attachment of its uploader, not yet part of any message.
/// </summary>
public sealed partial class Attachments(
    AppDbContext db,
    IFileStore files,
    TimeProvider time,
    ILogger<Attachments> logger)
{
    /// <summary>Returns the new attachment, or one of <see cref="ImageErrors"/>.</summary>
    public async Task<(Attachment? Attachment, string? Error)> UploadAsync(Guid ownerUserId, ReadOnlyMemory<byte> file, CancellationToken cancellationToken)
    {
        var (image, error) = ImageProcessor.Process(file.Span);
        if (image is null)
        {
            return (null, error);
        }

        var now = time.GetUtcNow();
        var container = FileContainers.Attachments;
        // Random names, unrelated to the attachment id and never taken from the uploaded file (§8.4).
        var blobName = $"{Guid.NewGuid():N}.webp";
        var thumbnailName = $"{Guid.NewGuid():N}.webp";

        await files.SaveAsync(container, blobName, image.Display, ProcessedImage.ContentType, cancellationToken);
        await files.SaveAsync(container, thumbnailName, image.Thumbnail, ProcessedImage.ContentType, cancellationToken);

        var attachment = Attachment.Uploaded(
            Guid.CreateVersion7(now), ownerUserId, container, blobName, thumbnailName, ProcessedImage.ContentType, image.Display.Length, image.Width, image.Height, now);
        db.Attachments.Add(attachment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await DeleteQuietlyAsync(container, blobName, thumbnailName);
            throw;
        }

        LogUploaded(logger, attachment.Id, ownerUserId, image.Display.Length);
        return (attachment, null);
    }

    /// <summary>
    /// The file, if this user may read it: the uploader; the customer of the conversation it was sent in; or an admin
    /// in a session that passed the admin policy (decided by the caller). Anyone else gets <c>null</c>, the same as
    /// for an id that does not exist, so ids cannot be probed.
    /// </summary>
    public async Task<ReadableFile?> FindReadableAsync(Guid attachmentId, Guid userId, bool isAdmin, bool thumbnail, CancellationToken cancellationToken)
    {
        var row = await db.Attachments.AsNoTracking()
            .Where(attachment => attachment.Id == attachmentId)
            .Select(attachment => new
            {
                attachment.OwnerUserId,
                attachment.Container,
                attachment.BlobName,
                attachment.ThumbnailBlobName,
                attachment.ContentType,
                CustomerId = db.Messages
                    .Where(message => message.Id == attachment.MessageId)
                    .Join(db.Conversations, message => message.ConversationId, conversation => conversation.Id, (_, conversation) => (Guid?)conversation.CustomerId)
                    .FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null || !(isAdmin || row.OwnerUserId == userId || row.CustomerId == userId))
        {
            return null;
        }

        return new ReadableFile(row.Container, thumbnail ? row.ThumbnailBlobName : row.BlobName, row.ContentType);
    }

    /// <summary>Deletes uploads that were never sent within a day (§5), rows and blobs. Returns how many.</summary>
    public async Task<int> DeleteAbandonedAsync(int batchSize, CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - Attachment.UnsentLifetime;
        var abandoned = await db.Attachments
            .Where(attachment => attachment.MessageId == null && attachment.CreatedAt <= cutoff)
            .OrderBy(attachment => attachment.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        if (abandoned.Count == 0)
        {
            return 0;
        }

        // Rows first: a blob without a row is harmless, a row without its blob would answer 404s.
        db.Attachments.RemoveRange(abandoned);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var attachment in abandoned)
        {
            await DeleteQuietlyAsync(attachment.Container, attachment.BlobName, attachment.ThumbnailBlobName);
        }

        return abandoned.Count;
    }

    private async Task DeleteQuietlyAsync(string container, params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                await files.DeleteAsync(container, name, CancellationToken.None);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                LogBlobNotDeleted(logger, error, container, name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Attachment {AttachmentId} uploaded by {UserId} ({Bytes} bytes)")]
    private static partial void LogUploaded(ILogger logger, Guid attachmentId, Guid userId, int bytes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Blob {Container}/{BlobName} could not be deleted")]
    private static partial void LogBlobNotDeleted(ILogger logger, Exception error, string container, string blobName);
}
