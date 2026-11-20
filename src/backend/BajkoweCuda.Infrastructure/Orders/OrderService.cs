using System.Globalization;
using System.Text.Json;
using BajkoweCuda.Application.Audit;
using BajkoweCuda.Application.Chat;
using BajkoweCuda.Application.Emails;
using BajkoweCuda.Application.Files;
using BajkoweCuda.Domain;
using BajkoweCuda.Domain.Chat;
using BajkoweCuda.Domain.Orders;
using BajkoweCuda.Infrastructure.Chat;
using BajkoweCuda.Infrastructure.Emails;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BajkoweCuda.Infrastructure.Orders;

/// <summary>The outcome of a change: the order for the admin, the system message posted in the chat (if any), and the summary for the events.</summary>
public sealed record OrderChange(AdminOrder Order, ChatMessage? SystemMessage, OrderSummary Summary, bool VisibleToCustomer);

/// <summary>
/// Orders (docs/PLAN.md §3.5): created by an admin from a conversation, changed through the domain's state machine.
/// Every step posts a system message in the conversation (rendered in the reader's language by the client), writes an
/// audit entry, and the confirmation emails the payment instructions to the customer. Customers see only their own
/// orders that are not drafts.
/// </summary>
public sealed partial class OrderService(
    AppDbContext db,
    IFileStore files,
    PaymentInstructions paymentInstructions,
    ChatDigests digests,
    IChatLinks links,
    IEmailOutbox emails,
    IAuditLog audit,
    TimeProvider time,
    ILogger<OrderService> logger)
{
    public const int DefaultPageSize = 30;
    public const int MaxPageSize = 100;

    /// <summary>System message event types, one per step (the client and the emails translate them).</summary>
    public static string EventType(OrderAction action) => action switch
    {
        OrderAction.Confirm => "order.confirmed",
        OrderAction.MarkPaid => "order.paid",
        OrderAction.MarkReady => "order.ready",
        OrderAction.MarkShipped => "order.shipped",
        OrderAction.Complete => "order.completed",
        _ => "order.cancelled",
    };

    /// <summary>A draft for the customer of the conversation. Rule violations throw <see cref="DomainException"/>.</summary>
    public async Task<OrderChange?> CreateAsync(
        Guid adminId,
        Guid conversationId,
        string title,
        string? description,
        decimal price,
        Guid? referenceImageId,
        string? adminNote,
        CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == conversationId, cancellationToken)
            ?? throw new DomainException(OrderApiErrors.ConversationNotFound, "The conversation does not exist.");
        if (referenceImageId is { } imageId)
        {
            await RequireUsableImageAsync(imageId, conversationId, adminId, cancellationToken);
        }

        var order = Order.CreateDraft(conversation.CustomerId, conversationId, title, description, Money.Euros(price), referenceImageId, adminNote, adminId, time.GetUtcNow());
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("orders.created", adminId, Entity(order), cancellationToken: cancellationToken);
        LogChanged(logger, order.Id, "created", adminId);
        return await ResultAsync(order.Id, null, cancellationToken);
    }

    /// <summary>Changes fields of the order. The concurrency token must match, or <see cref="DbUpdateConcurrencyException"/> is thrown.</summary>
    public async Task<OrderChange?> UpdateAsync(Guid id, Guid adminId, OrderChanges changes, byte[] rowVersion, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(id, rowVersion, cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (changes.Title is not null || changes.Description is not null)
        {
            order.EditText(changes.Title ?? order.Title, changes.Description ?? order.Description);
        }

        if (changes.Price is { } price && price != order.Price.Amount)
        {
            order.ChangePrice(Money.Euros(price));
        }

        if (changes.ClearReferenceImage && order.ReferenceImageId is not null)
        {
            order.ChangeReferenceImage(null);
        }
        else if (changes.ReferenceImageId is { } imageId && imageId != order.ReferenceImageId)
        {
            await RequireUsableImageAsync(imageId, order.ConversationId, adminId, cancellationToken);
            order.ChangeReferenceImage(imageId);
        }

        if (changes.AdminNote is not null && changes.AdminNote != (order.AdminNote ?? string.Empty))
        {
            order.ChangeAdminNote(changes.AdminNote);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("orders.updated", adminId, Entity(order), cancellationToken: cancellationToken);
        return await ResultAsync(order.Id, null, cancellationToken);
    }

    /// <summary>
    /// One step of the state machine. Posts the system message in the conversation, and on confirmation emails the
    /// payment instructions (always: the customer needs them, §3.6 decision in docs/DECISIONS.md).
    /// </summary>
    public async Task<OrderChange?> ApplyAsync(Guid id, Guid adminId, OrderAction action, string? reason, byte[]? rowVersion, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(id, rowVersion, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var eventType = EventType(action);
        _ = action switch
        {
            OrderAction.Confirm => order.Confirm(adminId, now),
            OrderAction.MarkPaid => order.MarkPaid(adminId, now),
            OrderAction.MarkReady => order.MarkReady(adminId, now),
            OrderAction.MarkShipped => order.MarkShipped(adminId, now),
            OrderAction.Complete => order.Complete(adminId, now),
            _ => order.Cancel(reason ?? string.Empty, adminId, now),
        };

        var customer = await db.Users.AsNoTracking()
            .Where(user => user.Id == order.CustomerId)
            .Select(user => new { user.Email, user.PreferredLanguage, user.IsDeleted })
            .SingleAsync(cancellationToken);
        var instructions = action == OrderAction.Confirm ? paymentInstructions.Render(order, customer.PreferredLanguage) : null;

        Message? system = null;
        if (order.ConversationId is { } conversationId
            && await db.Conversations.SingleOrDefaultAsync(row => row.Id == conversationId, cancellationToken) is { } conversation)
        {
            system = conversation.PostSystem(eventType, Payload(order, instructions), order.Id, now);
            db.Messages.Add(system);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync($"orders.{eventType[6..]}", adminId, Entity(order), cancellationToken: cancellationToken);
        LogChanged(logger, order.Id, eventType, adminId);

        if (action == OrderAction.Confirm && !customer.IsDeleted && customer.Email is not null)
        {
            await emails.EnqueueAsync(
                EmailTemplate.OrderConfirmed,
                order.CustomerId,
                customer.Email,
                customer.PreferredLanguage,
                new Dictionary<string, string>
                {
                    [EmailRenderer.LinkKey] = links.CustomerOrder(order.Id, customer.PreferredLanguage),
                    ["orderNumber"] = order.DisplayNumber,
                    ["title"] = order.Title,
                    ["amount"] = order.Price.Amount.ToString("N2", CultureInfo.GetCultureInfo(Languages.Find(customer.PreferredLanguage) ?? Languages.Default)) + " " + order.Price.Currency,
                    [EmailRenderer.DetailsKey] = instructions!,
                },
                cancellationToken);
        }
        else if (system is not null)
        {
            // The other steps reach the customer by email only with "send replies to my email" on (§3.6).
            await digests.QueueAsync(system.ConversationId, ChatSide.Studio, cancellationToken);
        }

        return await ResultAsync(order.Id, system, cancellationToken);
    }

    /// <summary>A photo of the finished toy, already uploaded and re-encoded (see <c>Attachments.UploadAsync</c>).</summary>
    public async Task<OrderChange?> AddPhotoAsync(Guid id, Guid adminId, Attachment upload, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(id, null, cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.AddPhoto(upload.Id, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("orders.photo_added", adminId, Entity(order), cancellationToken: cancellationToken);
        return await ResultAsync(order.Id, null, cancellationToken);
    }

    /// <summary>Removes a photo, and deletes its attachment and blobs.</summary>
    public async Task<OrderChange?> RemovePhotoAsync(Guid id, Guid adminId, Guid photoId, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(id, null, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var photo = order.RemovePhoto(photoId);
        var attachment = await db.Attachments.SingleAsync(row => row.Id == photo.AttachmentId, cancellationToken);
        db.OrderPhotos.Remove(photo);
        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync(cancellationToken);
        await DeleteBlobsAsync(attachment);
        await audit.WriteAsync("orders.photo_removed", adminId, Entity(order), cancellationToken: cancellationToken);
        return await ResultAsync(order.Id, null, cancellationToken);
    }

    public async Task<OrderChange?> ReorderPhotosAsync(Guid id, IReadOnlyList<Guid> photoIds, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(id, null, cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.ReorderPhotos(photoIds);
        await db.SaveChangesAsync(cancellationToken);
        return await ResultAsync(order.Id, null, cancellationToken);
    }

    /// <summary>The admin list, newest first; filter by status or by conversation (the side panel).</summary>
    public async Task<OrderPage> ListAsync(OrderStatus? status, Guid? conversationId, string? cursor, int take, CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, MaxPageSize);
        var offset = int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        var query = db.Orders.AsNoTracking();
        if (status is { } wanted)
        {
            query = query.Where(order => order.Status == wanted);
        }

        if (conversationId is { } conversation)
        {
            query = query.Where(order => order.ConversationId == conversation);
        }

        var rows = await query.OrderByDescending(order => order.CreatedAt).ThenByDescending(order => order.Id)
            .Skip(offset).Take(take + 1).ToListAsync(cancellationToken);
        var items = await SummariesAsync([.. rows.Take(take)], cancellationToken);
        return new OrderPage(items, rows.Count > take ? (offset + take).ToString(CultureInfo.InvariantCulture) : null);
    }

    public async Task<AdminOrder?> GetForAdminAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking().Include(row => row.Photos).Include(row => row.StatusChanges)
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        return order is null ? null : await AdminViewAsync(order, cancellationToken);
    }

    /// <summary>The customer's own orders that are not drafts, newest first.</summary>
    public async Task<IReadOnlyList<OrderSummary>> ListForCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var rows = await db.Orders.AsNoTracking()
            .Where(order => order.CustomerId == customerId && order.Status != OrderStatus.Draft)
            .OrderByDescending(order => order.CreatedAt).ThenByDescending(order => order.Id)
            .ToListAsync(cancellationToken);
        return await SummariesAsync(rows, cancellationToken);
    }

    public Task<bool> CustomerHasOrdersAsync(Guid customerId, CancellationToken cancellationToken) =>
        db.Orders.AnyAsync(order => order.CustomerId == customerId && order.Status != OrderStatus.Draft, cancellationToken);

    /// <summary>One of the customer's orders; <c>null</c> for someone else's order or a draft (no difference visible).</summary>
    public async Task<CustomerOrder?> GetForCustomerAsync(Guid customerId, Guid id, string language, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking().Include(row => row.Photos).Include(row => row.StatusChanges)
            .SingleOrDefaultAsync(row => row.Id == id && row.CustomerId == customerId && row.Status != OrderStatus.Draft, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var images = await ImagesAsync([.. order.Photos.Select(photo => photo.AttachmentId), .. Optional(order.ReferenceImageId)], cancellationToken);
        return new CustomerOrder(
            order.Id,
            order.DisplayNumber,
            order.Title,
            order.Description,
            order.Status.ToString(),
            OrderPrice.From(order.Price),
            order.CreatedAt,
            order.CancellationReason,
            order.ReferenceImageId is { } reference && images.TryGetValue(reference, out var referenceImage) ? referenceImage : null,
            [.. order.Photos.Where(photo => images.ContainsKey(photo.AttachmentId)).Select(photo => images[photo.AttachmentId])],
            Timeline(order, forCustomer: true),
            order.Status == OrderStatus.AwaitingPayment ? paymentInstructions.Render(order, language) : null);
    }

    /// <summary>Who may see an order image besides admins: the customer, once the order is no longer a draft.</summary>
    public Task<bool> CustomerSeesOrderImageAsync(Guid customerId, Guid attachmentId, CancellationToken cancellationToken) =>
        db.Orders.AnyAsync(
            order => order.CustomerId == customerId && order.Status != OrderStatus.Draft
                && (order.ReferenceImageId == attachmentId || db.OrderPhotos.Any(photo => photo.OrderId == order.Id && photo.AttachmentId == attachmentId)),
            cancellationToken);

    /// <summary>
    /// A reference image must be part of this conversation, or a fresh upload of the admin (not sent anywhere yet);
    /// anything else is refused with one code, so ids cannot be probed.
    /// </summary>
    private async Task RequireUsableImageAsync(Guid imageId, Guid? conversationId, Guid adminId, CancellationToken cancellationToken)
    {
        var usable = await db.Attachments.AnyAsync(
            attachment => attachment.Id == imageId
                && ((attachment.MessageId == null && attachment.OwnerUserId == adminId)
                    || db.Messages.Any(message => message.Id == attachment.MessageId && message.ConversationId == conversationId)),
            cancellationToken);
        if (!usable)
        {
            throw new DomainException(OrderApiErrors.ImageUnavailable, "The image is not available for this order.");
        }
    }

    private async Task<Order?> LoadAsync(Guid id, byte[]? rowVersion, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(row => row.Photos).Include(row => row.StatusChanges)
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (order is not null && rowVersion is not null)
        {
            // Compare with the version the admin saw: someone else's change in between makes SaveChanges throw.
            db.Entry(order).Property(row => row.RowVersion).OriginalValue = rowVersion;
        }

        return order;
    }

    private async Task<OrderChange> ResultAsync(Guid id, Message? system, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var order = await db.Orders.AsNoTracking().Include(row => row.Photos).Include(row => row.StatusChanges)
            .SingleAsync(row => row.Id == id, cancellationToken);
        var view = await AdminViewAsync(order, cancellationToken);
        var summary = (await SummariesAsync([order], cancellationToken))[0];
        return new OrderChange(view, system is null ? null : ChatMessage.Create(system, []), summary, order.IsVisibleToCustomer);
    }

    private async Task<AdminOrder> AdminViewAsync(Order order, CancellationToken cancellationToken)
    {
        var customerName = await db.Users.Where(user => user.Id == order.CustomerId).Select(user => user.DisplayName).SingleAsync(cancellationToken);
        var images = await ImagesAsync([.. order.Photos.Select(photo => photo.AttachmentId), .. Optional(order.ReferenceImageId)], cancellationToken);
        return new AdminOrder(
            order.Id,
            order.DisplayNumber,
            order.Title,
            order.Description,
            order.Status.ToString(),
            OrderPrice.From(order.Price),
            order.AdminNote,
            order.CancellationReason,
            order.CreatedAt,
            order.CustomerId,
            customerName,
            order.ConversationId,
            order.ReferenceImageId is { } reference && images.TryGetValue(reference, out var referenceImage) ? referenceImage : null,
            [.. order.Photos.Where(photo => images.ContainsKey(photo.AttachmentId)).Select(photo => new AdminOrderPhoto(photo.Id, images[photo.AttachmentId]))],
            Timeline(order, forCustomer: false),
            Convert.ToBase64String(order.RowVersion));
    }

    private async Task<List<OrderSummary>> SummariesAsync(List<Order> orders, CancellationToken cancellationToken)
    {
        var orderIds = orders.Select(order => order.Id).ToList();
        var firstPhotos = await db.OrderPhotos.AsNoTracking()
            .Where(photo => orderIds.Contains(photo.OrderId))
            .GroupBy(photo => photo.OrderId)
            .Select(group => group.OrderBy(photo => photo.SortOrder).First())
            .ToListAsync(cancellationToken);
        var thumbnailIds = orders
            .Select(order => firstPhotos.FirstOrDefault(photo => photo.OrderId == order.Id)?.AttachmentId ?? order.ReferenceImageId)
            .OfType<Guid>()
            .ToList();
        var images = await ImagesAsync(thumbnailIds, cancellationToken);

        return [.. orders.Select(order =>
        {
            var thumbnailId = firstPhotos.FirstOrDefault(photo => photo.OrderId == order.Id)?.AttachmentId ?? order.ReferenceImageId;
            return new OrderSummary(
                order.Id,
                order.DisplayNumber,
                order.Title,
                order.Status.ToString(),
                OrderPrice.From(order.Price),
                order.CreatedAt,
                order.CustomerId,
                order.ConversationId,
                thumbnailId is { } id && images.TryGetValue(id, out var image) ? image : null);
        })];
    }

    private async Task<Dictionary<Guid, ChatImage>> ImagesAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Attachments.AsNoTracking()
            .Where(attachment => ids.Contains(attachment.Id))
            .ToDictionaryAsync(attachment => attachment.Id, ChatImage.From, cancellationToken);
    }

    /// <summary>The customer's timeline starts at the confirmation; the studio sees the draft too.</summary>
    private static List<OrderStep> Timeline(Order order, bool forCustomer) =>
        [.. order.StatusChanges
            .Where(change => !forCustomer || change.ToStatus != OrderStatus.Draft)
            .OrderBy(change => change.ChangedAt).ThenBy(change => change.Id)
            .Select(change => new OrderStep(change.ToStatus.ToString(), change.ChangedAt, change.Comment))];

    /// <summary>The data of a system message; the client renders it in the reader's language.</summary>
    private static string Payload(Order order, string? paymentInstructions) => JsonSerializer.Serialize(new Dictionary<string, string?>
    {
        ["orderId"] = order.Id.ToString(),
        ["number"] = order.DisplayNumber,
        ["title"] = order.Title,
        ["amount"] = order.Price.Amount.ToString("0.00", CultureInfo.InvariantCulture),
        ["currency"] = order.Price.Currency,
        ["status"] = order.Status.ToString(),
        ["paymentInstructions"] = paymentInstructions,
        ["reason"] = order.CancellationReason,
    });

    private static AuditEntity Entity(Order order) => new("order", order.Id.ToString());

    private static IEnumerable<Guid> Optional(Guid? id) => id is { } value ? [value] : [];

    private async Task DeleteBlobsAsync(Attachment attachment)
    {
        foreach (var name in new[] { attachment.BlobName, attachment.ThumbnailBlobName })
        {
            try
            {
                await files.DeleteAsync(attachment.Container, name, CancellationToken.None);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                LogBlobNotDeleted(logger, error, attachment.Container, name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId}: {Change} by {AdminId}")]
    private static partial void LogChanged(ILogger logger, Guid orderId, string change, Guid adminId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Blob {Container}/{BlobName} could not be deleted")]
    private static partial void LogBlobNotDeleted(ILogger logger, Exception error, string container, string blobName);
}
