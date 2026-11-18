using System.Text.Json;
using BajkoweCuda.Domain.Chat;

namespace BajkoweCuda.Infrastructure.Chat;

// What the chat endpoints and the SignalR events return (docs/PLAN.md §6). Explicit records, no mapping library.

/// <summary>An image of a message; both addresses check access on every request (<c>/api/files</c>).</summary>
public sealed record ChatImage(Guid Id, string Url, string ThumbnailUrl, int Width, int Height)
{
    public static ChatImage From(Attachment attachment) => new(
        attachment.Id,
        $"/api/files/{attachment.Id}",
        $"/api/files/{attachment.Id}/thumbnail",
        attachment.Width,
        attachment.Height);
}

/// <summary>
/// A message as both sides see it. <c>from</c> is "customer" or "studio"; system messages carry an event type and a
/// payload that the client renders in the reader's language.
/// </summary>
public sealed record ChatMessage(
    Guid Id,
    Guid ConversationId,
    string From,
    string Kind,
    string Body,
    string? SystemEventType,
    JsonElement? SystemEventPayload,
    Guid? OrderId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadByCustomerAt,
    DateTimeOffset? ReadByAdminAt,
    IReadOnlyList<ChatImage> Images)
{
    public static ChatMessage Create(Message message, IEnumerable<Attachment> images) => new(
        message.Id,
        message.ConversationId,
        message.From == ChatSide.Customer ? "customer" : "studio",
        message.Kind == MessageKind.System ? "system" : "text",
        message.Body,
        message.SystemEventType,
        message.SystemEventPayload is null ? null : JsonDocument.Parse(message.SystemEventPayload).RootElement.Clone(),
        message.OrderId,
        message.CreatedAt,
        message.ReadByCustomerAt,
        message.ReadByAdminAt,
        [.. images.OrderBy(image => image.CreatedAt).ThenBy(image => image.Id).Select(ChatImage.From)]);
}

/// <summary>A page of history, newest first. Pass <c>nextCursor</c> as <c>before</c> for older messages.</summary>
public sealed record ChatPage(IReadOnlyList<ChatMessage> Items, Guid? NextCursor);

/// <summary><c>GET /api/chat</c>: the customer's conversation, if it exists yet, and their notification setting.</summary>
public sealed record CustomerChat(
    Guid? ConversationId,
    bool EmailNotificationsEnabled,
    bool EmailPromptAnswered,
    int UnreadCount);

public sealed record ChatCustomer(Guid Id, string DisplayName, string? Email, string PreferredLanguage, bool IsDeleted);

/// <summary>A row of the admin's conversation list (§3.4: unread first, then the newest).</summary>
public sealed record ConversationSummary(
    Guid Id,
    ChatCustomer Customer,
    DateTimeOffset LastMessageAt,
    string? LastMessagePreview,
    int LastMessageImageCount,
    string? LastMessageKind,
    int UnreadCount);

public sealed record ConversationPage(IReadOnlyList<ConversationSummary> Items, string? NextCursor);

/// <summary>The admin's view of one conversation, with the customer for the side panel.</summary>
public sealed record ConversationDetail(
    Guid Id,
    ChatCustomer Customer,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastMessageAt,
    bool CustomerEmailNotificationsEnabled,
    int UnreadCount);

/// <summary>What happened when a side read up to a message: for the other side's read ticks and the unread badges.</summary>
public sealed record ReadReceipt(Guid ConversationId, Guid CustomerId, Guid UpToMessageId, string By, DateTimeOffset ReadAt, int Marked);
