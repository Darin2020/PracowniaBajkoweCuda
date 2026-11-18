using System.Text.Json;
using BajkoweCuda.Application.Chat;
using BajkoweCuda.Domain.Chat;
using BajkoweCuda.Infrastructure.Emails;
using BajkoweCuda.Infrastructure.Identity;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BajkoweCuda.Infrastructure.Chat;

/// <summary>
/// Chat email notifications, batched (docs/PLAN.md §3.6). A new message queues one notification per recipient and
/// conversation, to be sent a few minutes later; further messages join the pending one. When it is due, it lists the
/// messages the recipient has still not read, or is skipped if they have read everything meanwhile.
/// Recipients: the admins for customer messages (always), the customer for studio and system messages (only with
/// "send replies to my email" on).
/// </summary>
public sealed class ChatDigests(AppDbContext db, IChatLinks links, EmailDispatchSignal signal, IOptions<EmailOptions> options, TimeProvider time)
{
    public const string Template = "ChatDigest";

    /// <summary>The most messages listed in one email; the rest are counted.</summary>
    public const int MaxListed = 10;

    public const int ExcerptLength = 200;

    /// <summary>Queues the notifications for a message just posted in the conversation.</summary>
    public async Task QueueAsync(Guid conversationId, ChatSide from, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.AsNoTracking().SingleAsync(row => row.Id == conversationId, cancellationToken);
        var recipients = from == ChatSide.Customer
            ? await Admins().ToListAsync(cancellationToken)
            : conversation.CustomerEmailNotificationsEnabled
                ? await db.Users.AsNoTracking().Where(user => user.Id == conversation.CustomerId && user.EmailConfirmed && !user.IsDeleted && user.Email != null).ToListAsync(cancellationToken)
                : [];
        if (recipients.Count == 0)
        {
            return;
        }

        var ids = recipients.Select(user => (Guid?)user.Id).ToList();
        var pending = await db.EmailOutbox
            .Where(message => message.Kind == EmailKind.ChatDigest && message.Status == EmailStatus.Pending
                && message.ConversationId == conversationId && ids.Contains(message.ToUserId))
            .Select(message => message.ToUserId)
            .ToListAsync(cancellationToken);

        var now = time.GetUtcNow();
        var sendAfter = now + TimeSpan.FromMinutes(Math.Max(0, options.Value.ChatDigestDelayMinutes));
        var queued = false;
        foreach (var user in recipients.Where(user => !pending.Contains(user.Id)))
        {
            var link = user.Id == conversation.CustomerId
                ? links.CustomerChat(user.PreferredLanguage)
                : links.AdminConversation(conversationId, user.PreferredLanguage);
            db.EmailOutbox.Add(new EmailOutboxMessage
            {
                Id = Guid.CreateVersion7(now),
                Kind = EmailKind.ChatDigest,
                ToUserId = user.Id,
                ToEmail = user.Email!,
                Language = user.PreferredLanguage,
                Template = Template,
                PayloadJson = JsonSerializer.Serialize(new Dictionary<string, string> { [EmailRenderer.LinkKey] = link }),
                ConversationId = conversationId,
                CreatedAt = now,
                SendAfter = sendAfter,
                Status = EmailStatus.Pending,
            });
            queued = true;
        }

        if (!queued)
        {
            return;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A message posted at the same moment queued the same notification first (unique pending index); it will
            // list this message too.
            foreach (var entry in db.ChangeTracker.Entries<EmailOutboxMessage>().Where(entry => entry.State == EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        signal.Notify();
    }

    /// <summary>
    /// The email for a due notification, or <c>null</c> when there is nothing to tell any more: everything is read,
    /// the customer turned notifications off, or the recipient or conversation is gone.
    /// </summary>
    public async Task<RenderedEmail?> ComposeAsync(EmailOutboxMessage notification, CancellationToken cancellationToken)
    {
        var conversation = notification.ConversationId is { } id
            ? await db.Conversations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken)
            : null;
        var recipient = notification.ToUserId is { } userId
            ? await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId, cancellationToken)
            : null;
        if (conversation is null || recipient is null || recipient.IsDeleted || recipient.Email is null)
        {
            return null;
        }

        var forCustomer = recipient.Id == conversation.CustomerId;
        if (forCustomer && !conversation.CustomerEmailNotificationsEnabled)
        {
            return null;
        }

        // Still an admin? (The role is revoked when the address leaves Admin:Emails.)
        if (!forCustomer && !await Admins().AnyAsync(user => user.Id == recipient.Id, cancellationToken))
        {
            return null;
        }

        // The studio reads as one side: once any admin has read the messages, no admin is notified about them.
        var unread = db.Messages.AsNoTracking().Where(message => message.ConversationId == conversation.Id);
        unread = forCustomer
            ? unread.Where(message => message.From == ChatSide.Studio && message.ReadByCustomerAt == null)
            : unread.Where(message => message.From == ChatSide.Customer && message.ReadByAdminAt == null);
        var total = await unread.CountAsync(cancellationToken);
        if (total == 0)
        {
            return null;
        }

        var listed = await unread
            .OrderBy(message => message.CreatedAt).ThenBy(message => message.Id)
            .Take(MaxListed)
            .Select(message => new
            {
                message.Body,
                message.Kind,
                Images = db.Attachments.Count(attachment => attachment.MessageId == message.Id),
            })
            .ToListAsync(cancellationToken);

        var customerName = forCustomer
            ? null
            : await db.Users.AsNoTracking().Where(user => user.Id == conversation.CustomerId).Select(user => user.DisplayName).SingleAsync(cancellationToken);
        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(notification.PayloadJson) ?? [];

        return EmailRenderer.RenderChatDigest(
            recipient.Email,
            recipient.PreferredLanguage,
            forCustomer,
            string.IsNullOrWhiteSpace(customerName) ? null : customerName,
            [.. listed.Select(message => new DigestItem(
                message.Kind == MessageKind.System,
                message.Body.Length <= ExcerptLength ? message.Body : string.Concat(message.Body.AsSpan(0, ExcerptLength - 1), "…"),
                message.Images))],
            total - listed.Count,
            payload.GetValueOrDefault(EmailRenderer.LinkKey) ?? throw new InvalidOperationException("A chat notification needs a link."));
    }

    /// <summary>Admins who can be emailed. The role is synced from Admin:Emails at sign-in (§3.3).</summary>
    private IQueryable<ApplicationUser> Admins() =>
        from user in db.Users.AsNoTracking()
        join userRole in db.UserRoles on user.Id equals userRole.UserId
        join role in db.Roles on userRole.RoleId equals role.Id
        where role.Name == Roles.Admin && user.EmailConfirmed && !user.IsDeleted && user.Email != null
        select user;
}
