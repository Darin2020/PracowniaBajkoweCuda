namespace BajkoweCuda.Domain.Orders;

public enum OrderStatus
{
    Draft = 0,
    AwaitingPayment = 1,
    InProgress = 2,
    Ready = 3,
    Shipped = 4,
    Completed = 5,
    Cancelled = 6,
}

/// <summary>
/// A custom toy order (docs/PLAN.md §3.5, §5). Created only by an admin, from a conversation, as a draft the customer
/// cannot see. Its life is a state machine:
/// <code>
/// Draft → AwaitingPayment → InProgress → Ready → Shipped → Completed
///   └──────────── cancel (with a reason) from any status that is not final ──────────→ Cancelled
/// </code>
/// Every step is recorded as an <see cref="OrderStatusChange"/>. Price, reference image, title and description can be
/// edited only while nothing has been paid (Draft, AwaitingPayment); the internal note at any time before the end.
/// </summary>
public sealed class Order
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int AdminNoteMaxLength = 2000;
    public const int ReasonMaxLength = 1000;
    public const int MaxPhotos = 20;

    private readonly List<OrderPhoto> _photos = [];
    private readonly List<OrderStatusChange> _statusChanges = [];

    private Order()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>The human number, from a database sequence; shown as <see cref="DisplayNumber"/> ("BC-0012").</summary>
    public int Number { get; private set; }

    public string DisplayNumber => FormatNumber(Number);

    public Guid CustomerId { get; private set; }

    /// <summary>The conversation it came from; null once the account of the customer (and its chat) has been deleted.</summary>
    public Guid? ConversationId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>
    /// An image of the conversation, or one uploaded for the order (an <c>Attachment</c> id); null when none was chosen
    /// or the image was deleted together with the account of the customer.
    /// </summary>
    public Guid? ReferenceImageId { get; private set; }

    public Money Price { get; private set; }

    public OrderStatus Status { get; private set; }

    /// <summary>For the studio only; never shown to the customer.</summary>
    public string? AdminNote { get; private set; }

    /// <summary>Shown to the customer.</summary>
    public string? CancellationReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? ReadyAt { get; private set; }

    public DateTimeOffset? ShippedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Optimistic concurrency (two admins changing the same order); set by the database.</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Photos of the finished toy, in display order.</summary>
    public IReadOnlyList<OrderPhoto> Photos => _photos.OrderBy(photo => photo.SortOrder).ToList();

    public IReadOnlyList<OrderStatusChange> StatusChanges => _statusChanges;

    /// <summary>Whether the customer may see it at all: drafts are the studio's work in progress.</summary>
    public bool IsVisibleToCustomer => Status != OrderStatus.Draft;

    public bool IsFinal => Status is OrderStatus.Completed or OrderStatus.Cancelled;

    public static string FormatNumber(int number) => $"BC-{number:D4}";

    public static Order CreateDraft(
        Guid customerId,
        Guid conversationId,
        string title,
        string? description,
        Money price,
        Guid? referenceImageId,
        string? adminNote,
        Guid adminId,
        DateTimeOffset now)
    {
        var order = new Order
        {
            Id = Guid.CreateVersion7(now),
            CustomerId = customerId,
            ConversationId = conversationId,
            Price = price,
            ReferenceImageId = referenceImageId,
            Status = OrderStatus.Draft,
            CreatedAt = now,
        };
        order.SetText(title, description);
        order.AdminNote = Optional(adminNote, AdminNoteMaxLength);
        order._statusChanges.Add(OrderStatusChange.Record(order.Id, null, OrderStatus.Draft, adminId, now, null));
        return order;
    }

    /// <summary>Title and description, while nothing has been paid.</summary>
    public void EditText(string title, string? description)
    {
        RequireEditableTerms();
        SetText(title, description);
    }

    /// <summary>The price, while nothing has been paid.</summary>
    public void ChangePrice(Money price)
    {
        RequireEditableTerms();
        Price = price;
    }

    /// <summary>The reference image, while nothing has been paid.</summary>
    public void ChangeReferenceImage(Guid? attachmentId)
    {
        RequireEditableTerms();
        ReferenceImageId = attachmentId;
    }

    /// <summary>The internal note, until the order has ended.</summary>
    public void ChangeAdminNote(string? note)
    {
        if (IsFinal)
        {
            throw new DomainException(OrderErrors.NotEditable, "A finished order cannot be changed.");
        }

        AdminNote = Optional(note, AdminNoteMaxLength);
    }

    /// <summary>Draft → AwaitingPayment: the customer can now see the order and the payment instructions.</summary>
    public OrderStatusChange Confirm(Guid adminId, DateTimeOffset now)
    {
        var change = Move(OrderStatus.Draft, OrderStatus.AwaitingPayment, adminId, now, null);
        ConfirmedAt = now;
        return change;
    }

    /// <summary>AwaitingPayment → InProgress.</summary>
    public OrderStatusChange MarkPaid(Guid adminId, DateTimeOffset now)
    {
        var change = Move(OrderStatus.AwaitingPayment, OrderStatus.InProgress, adminId, now, null);
        PaidAt = now;
        return change;
    }

    /// <summary>InProgress → Ready, which needs at least one photo of the finished toy.</summary>
    public OrderStatusChange MarkReady(Guid adminId, DateTimeOffset now)
    {
        RequireStatus(OrderStatus.InProgress);
        if (_photos.Count == 0)
        {
            throw new DomainException(OrderErrors.PhotoRequired, "Add at least one photo of the finished toy first.");
        }

        var change = Move(OrderStatus.InProgress, OrderStatus.Ready, adminId, now, null);
        ReadyAt = now;
        return change;
    }

    /// <summary>Ready → Shipped.</summary>
    public OrderStatusChange MarkShipped(Guid adminId, DateTimeOffset now)
    {
        var change = Move(OrderStatus.Ready, OrderStatus.Shipped, adminId, now, null);
        ShippedAt = now;
        return change;
    }

    /// <summary>Shipped → Completed.</summary>
    public OrderStatusChange Complete(Guid adminId, DateTimeOffset now)
    {
        var change = Move(OrderStatus.Shipped, OrderStatus.Completed, adminId, now, null);
        CompletedAt = now;
        return change;
    }

    /// <summary>Any status that is not final → Cancelled. The reason is shown to the customer.</summary>
    public OrderStatusChange Cancel(string reason, Guid adminId, DateTimeOffset now)
    {
        var text = reason?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            throw new DomainException(OrderErrors.ReasonRequired, "Cancelling needs a reason for the customer.");
        }

        if (text.Length > ReasonMaxLength)
        {
            throw new DomainException(OrderErrors.TooLong, $"The reason has at most {ReasonMaxLength} characters.");
        }

        if (IsFinal)
        {
            throw new DomainException(OrderErrors.InvalidTransition, $"A {Status} order cannot be cancelled.");
        }

        var change = OrderStatusChange.Record(Id, Status, OrderStatus.Cancelled, adminId, now, text);
        _statusChanges.Add(change);
        Status = OrderStatus.Cancelled;
        CancellationReason = text;
        CancelledAt = now;
        return change;
    }

    /// <summary>A photo of the finished toy (an uploaded <c>Attachment</c>), while it is being made or ready.</summary>
    public OrderPhoto AddPhoto(Guid attachmentId, DateTimeOffset now)
    {
        RequirePhotosOpen();
        if (_photos.Count >= MaxPhotos)
        {
            throw new DomainException(OrderErrors.TooManyPhotos, $"An order has at most {MaxPhotos} photos.");
        }

        var photo = OrderPhoto.Create(Id, attachmentId, _photos.Count == 0 ? 0 : _photos.Max(item => item.SortOrder) + 1, now);
        _photos.Add(photo);
        return photo;
    }

    public OrderPhoto RemovePhoto(Guid photoId)
    {
        RequirePhotosOpen();
        var photo = _photos.SingleOrDefault(item => item.Id == photoId)
            ?? throw new DomainException(OrderErrors.PhotoNotFound, "The photo is not part of this order.");
        if (Status == OrderStatus.Ready && _photos.Count == 1)
        {
            throw new DomainException(OrderErrors.PhotoRequired, "A ready order keeps at least one photo.");
        }

        _photos.Remove(photo);
        return photo;
    }

    /// <summary>Puts the photos in the given order; every photo of the order must be listed once.</summary>
    public void ReorderPhotos(IReadOnlyList<Guid> photoIds)
    {
        RequirePhotosOpen();
        if (photoIds.Count != _photos.Count || photoIds.Distinct().Count() != photoIds.Count
            || photoIds.Any(id => _photos.All(photo => photo.Id != id)))
        {
            throw new DomainException(OrderErrors.PhotoNotFound, "List every photo of the order exactly once.");
        }

        for (var position = 0; position < photoIds.Count; position++)
        {
            _photos.Single(photo => photo.Id == photoIds[position]).MoveTo(position);
        }
    }

    private OrderStatusChange Move(OrderStatus from, OrderStatus to, Guid adminId, DateTimeOffset now, string? comment)
    {
        RequireStatus(from);
        var change = OrderStatusChange.Record(Id, from, to, adminId, now, comment);
        _statusChanges.Add(change);
        Status = to;
        return change;
    }

    private void RequireStatus(OrderStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainException(OrderErrors.InvalidTransition, $"This step needs a {expected} order; it is {Status}.");
        }
    }

    private void RequireEditableTerms()
    {
        if (Status is not (OrderStatus.Draft or OrderStatus.AwaitingPayment))
        {
            throw new DomainException(OrderErrors.NotEditable, "The terms of an order cannot change after payment.");
        }
    }

    private void RequirePhotosOpen()
    {
        if (Status is not (OrderStatus.InProgress or OrderStatus.Ready))
        {
            throw new DomainException(OrderErrors.PhotosLocked, "Photos belong to orders that are being made or are ready.");
        }
    }

    private void SetText(string title, string? description)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainException(OrderErrors.TitleRequired, "An order needs a title.");
        }

        if (trimmed.Length > TitleMaxLength)
        {
            throw new DomainException(OrderErrors.TooLong, $"The title has at most {TitleMaxLength} characters.");
        }

        Title = trimmed;
        Description = Optional(description, DescriptionMaxLength) ?? string.Empty;
    }

    private static string? Optional(string? text, int maxLength)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength
            ? trimmed
            : throw new DomainException(OrderErrors.TooLong, $"The text has at most {maxLength} characters.");
    }
}

/// <summary>A photo of the finished toy (docs/PLAN.md §5, OrderPhoto).</summary>
public sealed class OrderPhoto
{
    private OrderPhoto()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid AttachmentId { get; private set; }

    public int SortOrder { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal static OrderPhoto Create(Guid orderId, Guid attachmentId, int sortOrder, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        OrderId = orderId,
        AttachmentId = attachmentId,
        SortOrder = sortOrder,
        CreatedAt = now,
    };

    internal void MoveTo(int sortOrder) => SortOrder = sortOrder;
}

/// <summary>One step in an order's life (docs/PLAN.md §5, OrderStatusChange): the customer's timeline and the audit trail.</summary>
public sealed class OrderStatusChange
{
    private OrderStatusChange()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    /// <summary><c>null</c> for the creation of the draft.</summary>
    public OrderStatus? FromStatus { get; private set; }

    public OrderStatus ToStatus { get; private set; }

    public Guid ChangedById { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>The cancellation reason; empty otherwise.</summary>
    public string? Comment { get; private set; }

    internal static OrderStatusChange Record(Guid orderId, OrderStatus? from, OrderStatus to, Guid changedById, DateTimeOffset now, string? comment) => new()
    {
        Id = Guid.CreateVersion7(now),
        OrderId = orderId,
        FromStatus = from,
        ToStatus = to,
        ChangedById = changedById,
        ChangedAt = now,
        Comment = comment,
    };
}
