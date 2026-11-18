namespace BajkoweCuda.Domain.Chat;

public enum MessageKind
{
    Text = 0,

    /// <summary>Posted automatically for order events; not editable, shown differently (§3.4).</summary>
    System = 1,
}

/// <summary>A message in a conversation (docs/PLAN.md §5). Messages are never edited or deleted by users.</summary>
public sealed class Message
{
    public const int BodyMaxLength = 2000;
    public const int MaxAttachments = 5;
    public const int SystemEventTypeMaxLength = 64;
    public const int SystemEventPayloadMaxLength = 4000;

    private Message()
    {
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    /// <summary>The author (an <c>ApplicationUser</c> id); <c>null</c> for system messages.</summary>
    public Guid? SenderId { get; private set; }

    /// <summary>Which side wrote it; <see cref="ChatSide.Studio"/> for system messages.</summary>
    public ChatSide From { get; private set; }

    public MessageKind Kind { get; private set; }

    /// <summary>The text, trimmed; empty for a message of images only and for system messages.</summary>
    public string Body { get; private set; } = string.Empty;

    public string? SystemEventType { get; private set; }

    public string? SystemEventPayload { get; private set; }

    public Guid? OrderId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadByCustomerAt { get; private set; }

    public DateTimeOffset? ReadByAdminAt { get; private set; }

    internal static Message Text(Conversation conversation, ChatSide from, Guid senderId, string? body, int attachmentCount, DateTimeOffset now)
    {
        var text = body?.Trim() ?? string.Empty;
        if (text.Length == 0 && attachmentCount == 0)
        {
            throw new DomainException(ChatErrors.EmptyMessage, "A message needs text or at least one image.");
        }

        if (text.Length > BodyMaxLength)
        {
            throw new DomainException(ChatErrors.MessageTooLong, $"A message has at most {BodyMaxLength} characters.");
        }

        if (attachmentCount > MaxAttachments)
        {
            throw new DomainException(ChatErrors.TooManyAttachments, $"A message has at most {MaxAttachments} images.");
        }

        var message = new Message
        {
            Id = Guid.CreateVersion7(now),
            ConversationId = conversation.Id,
            SenderId = senderId,
            From = from,
            Kind = MessageKind.Text,
            Body = text,
            CreatedAt = now,
        };
        message.MarkReadBy(from, now);
        return message;
    }

    internal static Message System(Conversation conversation, string eventType, string payloadJson, Guid? orderId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        if (eventType.Length > SystemEventTypeMaxLength || payloadJson.Length > SystemEventPayloadMaxLength)
        {
            throw new ArgumentException("The system event is too long.", nameof(payloadJson));
        }

        // The studio caused the event, so it counts as read on its side.
        return new Message
        {
            Id = Guid.CreateVersion7(now),
            ConversationId = conversation.Id,
            From = ChatSide.Studio,
            Kind = MessageKind.System,
            SystemEventType = eventType,
            SystemEventPayload = payloadJson,
            OrderId = orderId,
            CreatedAt = now,
            ReadByAdminAt = now,
        };
    }

    /// <summary>Whether the reader on that side still has to read it.</summary>
    public bool IsUnreadBy(ChatSide reader) => (reader == ChatSide.Customer ? ReadByCustomerAt : ReadByAdminAt) is null;

    /// <summary>Marks the message read on the reader's side; the first time counts.</summary>
    public void MarkReadBy(ChatSide reader, DateTimeOffset now)
    {
        if (reader == ChatSide.Customer)
        {
            ReadByCustomerAt ??= now;
        }
        else
        {
            ReadByAdminAt ??= now;
        }
    }
}
