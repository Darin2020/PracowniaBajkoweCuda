using System.Globalization;
using BajkoweCuda.Domain;
using BajkoweCuda.Domain.Chat;
using BajkoweCuda.Infrastructure.Identity;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BajkoweCuda.Infrastructure.Chat;

/// <summary>
/// The chat's reads and writes (docs/PLAN.md §3.4). Customers only ever reach their own conversation (found by their
/// user id, never by an id they send); admins reach any conversation by id. Message bodies are never logged.
/// </summary>
public sealed partial class ChatService(AppDbContext db, ChatDigests digests, TimeProvider time, ILogger<ChatService> logger)
{
    public const int DefaultPageSize = 30;
    public const int MaxPageSize = 100;
    public const int PreviewLength = 120;

    public async Task<CustomerChat> GetCustomerChatAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(row => row.CustomerId == customerId, cancellationToken);
        return conversation is null
            ? new CustomerChat(null, false, false, 0)
            : new CustomerChat(
                conversation.Id,
                conversation.CustomerEmailNotificationsEnabled,
                conversation.EmailPromptAnswered,
                await UnreadAsync(conversation.Id, ChatSide.Customer, cancellationToken));
    }

    public Task<Guid?> FindConversationIdAsync(Guid customerId, CancellationToken cancellationToken) =>
        db.Conversations.Where(row => row.CustomerId == customerId).Select(row => (Guid?)row.Id).SingleOrDefaultAsync(cancellationToken);

    public Task<Guid?> FindCustomerIdAsync(Guid conversationId, CancellationToken cancellationToken) =>
        db.Conversations.Where(row => row.Id == conversationId).Select(row => (Guid?)row.CustomerId).SingleOrDefaultAsync(cancellationToken);

    /// <summary>History newest first; <paramref name="before"/> is the oldest message id of the previous page.</summary>
    public async Task<ChatPage> PageAsync(Guid conversationId, Guid? before, int take, CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, MaxPageSize);
        var query = db.Messages.AsNoTracking().Where(message => message.ConversationId == conversationId);
        if (before is { } cursorId)
        {
            var cursor = await db.Messages.AsNoTracking()
                .Where(message => message.Id == cursorId && message.ConversationId == conversationId)
                .Select(message => new { message.CreatedAt, message.Id })
                .SingleOrDefaultAsync(cancellationToken);
            if (cursor is null)
            {
                return new ChatPage([], null);
            }

            query = query.Where(message => message.CreatedAt < cursor.CreatedAt
                || (message.CreatedAt == cursor.CreatedAt && message.Id.CompareTo(cursor.Id) < 0));
        }

        var rows = await query
            .OrderByDescending(message => message.CreatedAt).ThenByDescending(message => message.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken);
        var page = rows.Take(take).ToList();
        var messages = await WithImagesAsync(page, cancellationToken);
        return new ChatPage(messages, rows.Count > take ? page[^1].Id : null);
    }

    /// <summary>
    /// A customer's message. Their conversation is started by the first one (§3.4). Rule violations throw
    /// <see cref="DomainException"/>.
    /// </summary>
    public async Task<(ChatMessage Message, bool StartedConversation)> PostAsCustomerAsync(
        Guid customerId, string? body, IReadOnlyCollection<Guid> imageIds, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var conversation = await db.Conversations.SingleOrDefaultAsync(row => row.CustomerId == customerId, cancellationToken);
        var started = conversation is null;
        if (conversation is null)
        {
            conversation = Conversation.Start(customerId, now);
            db.Conversations.Add(conversation);
        }

        var images = await ImagesToSendAsync(customerId, imageIds, cancellationToken);
        var message = conversation.PostText(ChatSide.Customer, customerId, body, images, now);
        db.Messages.Add(message);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (started)
        {
            // Two first messages at once (two tabs): the other one started the conversation. Post into it.
            db.ChangeTracker.Clear();
            return (await PostIntoExistingAsync(customerId, body, imageIds, cancellationToken), false);
        }

        LogPosted(logger, message.Id, conversation.Id, "customer", images.Count);
        await digests.QueueAsync(conversation.Id, ChatSide.Customer, cancellationToken);
        return (ChatMessage.Create(message, images), started);
    }

    /// <summary>A reply of the studio; <c>null</c> when the conversation does not exist.</summary>
    public async Task<ChatMessage?> PostAsStudioAsync(
        Guid conversationId, Guid adminId, string? body, IReadOnlyCollection<Guid> imageIds, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(row => row.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        var images = await ImagesToSendAsync(adminId, imageIds, cancellationToken);
        var message = conversation.PostText(ChatSide.Studio, adminId, body, images, time.GetUtcNow());
        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken);
        LogPosted(logger, message.Id, conversation.Id, "studio", images.Count);
        await digests.QueueAsync(conversation.Id, ChatSide.Studio, cancellationToken);
        return ChatMessage.Create(message, images);
    }

    /// <summary>
    /// Marks the other side's messages up to and including <paramref name="upToMessageId"/> as read by
    /// <paramref name="reader"/>. <c>null</c> when that message is not in the conversation.
    /// </summary>
    public async Task<ReadReceipt?> MarkReadAsync(Guid conversationId, ChatSide reader, Guid upToMessageId, CancellationToken cancellationToken)
    {
        var target = await db.Messages.AsNoTracking()
            .Where(message => message.Id == upToMessageId && message.ConversationId == conversationId)
            .Select(message => new { message.CreatedAt })
            .SingleOrDefaultAsync(cancellationToken);
        var customerId = await FindCustomerIdAsync(conversationId, cancellationToken);
        if (target is null || customerId is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var unread = db.Messages.Where(message => message.ConversationId == conversationId && message.CreatedAt <= target.CreatedAt);
        var marked = reader == ChatSide.Customer
            ? await unread.Where(message => message.ReadByCustomerAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.ReadByCustomerAt, now), cancellationToken)
            : await unread.Where(message => message.ReadByAdminAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.ReadByAdminAt, now), cancellationToken);

        return new ReadReceipt(conversationId, customerId.Value, upToMessageId, reader == ChatSide.Customer ? "customer" : "studio", now, marked);
    }

    /// <summary>"Send replies to my email" (§3.6); <c>false</c> before the first message (no conversation yet).</summary>
    public async Task<bool> SetEmailNotificationsAsync(Guid customerId, bool enabled, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(row => row.CustomerId == customerId, cancellationToken);
        if (conversation is null)
        {
            return false;
        }

        conversation.SetEmailNotifications(enabled);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Messages of the other side that this side has not read: the customer's badge, or one conversation's count for admins.</summary>
    public Task<int> UnreadAsync(Guid conversationId, ChatSide reader, CancellationToken cancellationToken) =>
        reader == ChatSide.Customer
            ? db.Messages.CountAsync(message => message.ConversationId == conversationId && message.ReadByCustomerAt == null, cancellationToken)
            : db.Messages.CountAsync(message => message.ConversationId == conversationId && message.ReadByAdminAt == null, cancellationToken);

    /// <summary>The customer's badge; 0 without a conversation.</summary>
    public async Task<int> CustomerUnreadAsync(Guid customerId, CancellationToken cancellationToken) =>
        await FindConversationIdAsync(customerId, cancellationToken) is { } conversationId
            ? await UnreadAsync(conversationId, ChatSide.Customer, cancellationToken)
            : 0;

    /// <summary>Customer messages no admin has read yet, over all conversations (the admins' badge).</summary>
    public Task<int> StudioUnreadAsync(CancellationToken cancellationToken) =>
        db.Messages.CountAsync(message => message.ReadByAdminAt == null, cancellationToken);

    /// <summary>
    /// The admin's list: conversations with unread messages first, then by the newest message. <paramref name="search"/>
    /// matches the customer's name or email address. The cursor is an offset (the list order changes as messages
    /// arrive, so keyset paging would not be stable either).
    /// </summary>
    public async Task<ConversationPage> ListAsync(string? search, string? cursor, int take, CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, MaxPageSize);
        var offset = int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

        var query =
            from conversation in db.Conversations.AsNoTracking()
            join user in db.Users.AsNoTracking() on conversation.CustomerId equals user.Id
            select new { conversation, user };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(row => row.user.DisplayName.Contains(term) || (row.user.Email != null && row.user.Email.Contains(term)));
        }

        var rows = await query
            .Select(row => new
            {
                row.conversation.Id,
                row.conversation.LastMessageAt,
                Customer = new ChatCustomer(row.user.Id, row.user.DisplayName, row.user.Email, row.user.PreferredLanguage, row.user.IsDeleted),
                Unread = db.Messages.Count(message => message.ConversationId == row.conversation.Id && message.ReadByAdminAt == null),
                Last = db.Messages
                    .Where(message => message.ConversationId == row.conversation.Id)
                    .OrderByDescending(message => message.CreatedAt).ThenByDescending(message => message.Id)
                    .Select(message => new
                    {
                        message.Body,
                        message.Kind,
                        Images = db.Attachments.Count(attachment => attachment.MessageId == message.Id),
                    })
                    .FirstOrDefault(),
            })
            .OrderByDescending(row => row.Unread > 0).ThenByDescending(row => row.LastMessageAt).ThenBy(row => row.Id)
            .Skip(offset)
            .Take(take + 1)
            .ToListAsync(cancellationToken);

        var items = rows.Take(take).Select(row => new ConversationSummary(
            row.Id,
            row.Customer,
            row.LastMessageAt,
            row.Last is null ? null : Preview(row.Last.Body),
            row.Last?.Images ?? 0,
            row.Last is null ? null : row.Last.Kind == MessageKind.System ? "system" : "text",
            row.Unread)).ToList();
        return new ConversationPage(items, rows.Count > take ? (offset + take).ToString(CultureInfo.InvariantCulture) : null);
    }

    public async Task<ConversationDetail?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var row = await (
                from conversation in db.Conversations.AsNoTracking()
                join user in db.Users.AsNoTracking() on conversation.CustomerId equals user.Id
                where conversation.Id == conversationId
                select new { conversation, user })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        return new ConversationDetail(
            row.conversation.Id,
            Customer(row.user),
            row.conversation.CreatedAt,
            row.conversation.LastMessageAt,
            row.conversation.CustomerEmailNotificationsEnabled,
            await UnreadAsync(row.conversation.Id, ChatSide.Studio, cancellationToken));
    }

    private static ChatCustomer Customer(ApplicationUser user) =>
        new(user.Id, user.DisplayName, user.Email, user.PreferredLanguage, user.IsDeleted);

    private static string Preview(string body) => body.Length <= PreviewLength ? body : string.Concat(body.AsSpan(0, PreviewLength - 1), "…");

    private async Task<ChatMessage> PostIntoExistingAsync(Guid customerId, string? body, IReadOnlyCollection<Guid> imageIds, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.SingleAsync(row => row.CustomerId == customerId, cancellationToken);
        var images = await ImagesToSendAsync(customerId, imageIds, cancellationToken);
        var message = conversation.PostText(ChatSide.Customer, customerId, body, images, time.GetUtcNow());
        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken);
        LogPosted(logger, message.Id, conversation.Id, "customer", images.Count);
        await digests.QueueAsync(conversation.Id, ChatSide.Customer, cancellationToken);
        return ChatMessage.Create(message, images);
    }

    /// <summary>
    /// The sender's own uploads that are not part of a message yet. Anything else (someone else's image, an image
    /// already sent, an unknown id) is refused with one error code, so ids cannot be probed.
    /// </summary>
    private async Task<List<Attachment>> ImagesToSendAsync(Guid senderId, IReadOnlyCollection<Guid> imageIds, CancellationToken cancellationToken)
    {
        var ids = imageIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        if (ids.Count > Message.MaxAttachments)
        {
            throw new DomainException(ChatErrors.TooManyAttachments, $"A message has at most {Message.MaxAttachments} images.");
        }

        var images = await db.Attachments
            .Where(attachment => ids.Contains(attachment.Id) && attachment.OwnerUserId == senderId && attachment.MessageId == null)
            .ToListAsync(cancellationToken);
        if (images.Count != ids.Count)
        {
            throw new DomainException(ChatErrors.AttachmentUnavailable, "An image is not available for this message.");
        }

        // In the order the sender chose.
        return [.. ids.Select(id => images.Single(image => image.Id == id))];
    }

    private async Task<List<ChatMessage>> WithImagesAsync(List<Message> messages, CancellationToken cancellationToken)
    {
        var ids = messages.Select(message => (Guid?)message.Id).ToList();
        var images = (await db.Attachments.AsNoTracking().Where(attachment => ids.Contains(attachment.MessageId)).ToListAsync(cancellationToken))
            .ToLookup(attachment => attachment.MessageId!.Value);
        return [.. messages.Select(message => ChatMessage.Create(message, images[message.Id]))];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Message {MessageId} posted in {ConversationId} by the {Side} with {ImageCount} images")]
    private static partial void LogPosted(ILogger logger, Guid messageId, Guid conversationId, string side, int imageCount);
}
