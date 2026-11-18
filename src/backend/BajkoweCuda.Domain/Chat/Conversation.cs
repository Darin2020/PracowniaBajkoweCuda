namespace BajkoweCuda.Domain.Chat;

/// <summary>
/// The one conversation between a customer and the studio (docs/PLAN.md §3.4, §5). It is started by the customer's
/// first message. Messages are not loaded with it: there can be thousands, and the API pages through them.
/// </summary>
public sealed class Conversation
{
    private Conversation()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>The customer (an <c>ApplicationUser</c> id). Unique: one conversation per customer.</summary>
    public Guid CustomerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Sorts the admin's conversation list.</summary>
    public DateTimeOffset LastMessageAt { get; private set; }

    /// <summary>"Send replies to my email" (§3.6); off until the customer says yes.</summary>
    public bool CustomerEmailNotificationsEnabled { get; private set; }

    /// <summary>Whether the customer has answered the question asked after their first message.</summary>
    public bool EmailPromptAnswered { get; private set; }

    public static Conversation Start(Guid customerId, DateTimeOffset now)
    {
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("A conversation needs a customer.", nameof(customerId));
        }

        return new Conversation
        {
            Id = Guid.CreateVersion7(now),
            CustomerId = customerId,
            CreatedAt = now,
            LastMessageAt = now,
        };
    }

    /// <summary>
    /// Posts a text message with up to five uploaded images, which become part of it. The sender has read their own
    /// message.
    /// </summary>
    public Message PostText(ChatSide from, Guid senderId, string? body, IReadOnlyCollection<Attachment> attachments, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        if (from == ChatSide.Customer && senderId != CustomerId)
        {
            throw new InvalidOperationException("Only the conversation's customer writes on the customer side.");
        }

        var message = Message.Text(this, from, senderId, body, attachments.Count, now);
        foreach (var attachment in attachments)
        {
            attachment.AttachTo(message, senderId);
        }

        LastMessageAt = now;
        return message;
    }

    /// <summary>
    /// Posts a message of the system about an order event (§3.5). The payload is rendered in the reader's language
    /// by the client, so it holds data, not text.
    /// </summary>
    public Message PostSystem(string eventType, string payloadJson, Guid? orderId, DateTimeOffset now)
    {
        var message = Message.System(this, eventType, payloadJson, orderId, now);
        LastMessageAt = now;
        return message;
    }

    /// <summary>Turns "send replies to my email" on or off; this also answers the first-message question.</summary>
    public void SetEmailNotifications(bool enabled)
    {
        CustomerEmailNotificationsEnabled = enabled;
        EmailPromptAnswered = true;
    }
}
