using BajkoweCuda.Domain.Orders;
using BajkoweCuda.Infrastructure.Chat;

namespace BajkoweCuda.Infrastructure.Orders;

// What the order endpoints and the OrderUpdated event return (docs/PLAN.md §6). Explicit records, no mapping library.

public sealed record OrderPrice(decimal Amount, string Currency)
{
    public static OrderPrice From(Money money) => new(money.Amount, money.Currency);
}

/// <summary>One step of the timeline: the status and when it was reached.</summary>
public sealed record OrderStep(string Status, DateTimeOffset At, string? Comment);

/// <summary>A row of a list, and the payload of the <c>OrderUpdated</c> event.</summary>
public sealed record OrderSummary(
    Guid Id,
    string Number,
    string Title,
    string Status,
    OrderPrice Price,
    DateTimeOffset CreatedAt,
    Guid CustomerId,
    string CustomerName,
    Guid? ConversationId,
    ChatImage? Thumbnail);

/// <summary>The customer's view (<c>/api/orders</c>): no internal note; drafts never reach it.</summary>
public sealed record CustomerOrder(
    Guid Id,
    string Number,
    string Title,
    string Description,
    string Status,
    OrderPrice Price,
    DateTimeOffset CreatedAt,
    string? CancellationReason,
    ChatImage? ReferenceImage,
    IReadOnlyList<ChatImage> Photos,
    IReadOnlyList<OrderStep> Timeline,
    string? PaymentInstructions);

public sealed record AdminOrderPhoto(Guid Id, ChatImage Image);

/// <summary>The studio's view (<c>/api/admin/orders</c>), with the internal note and the concurrency token.</summary>
public sealed record AdminOrder(
    Guid Id,
    string Number,
    string Title,
    string Description,
    string Status,
    OrderPrice Price,
    string? AdminNote,
    string? CancellationReason,
    DateTimeOffset CreatedAt,
    Guid CustomerId,
    string CustomerName,
    Guid? ConversationId,
    ChatImage? ReferenceImage,
    IReadOnlyList<AdminOrderPhoto> Photos,
    IReadOnlyList<OrderStep> Timeline,
    string RowVersion);

public sealed record OrderPage(IReadOnlyList<OrderSummary> Items, string? NextCursor);

/// <summary>The steps an admin can take (<c>POST /api/admin/orders/{id}/{step}</c>).</summary>
public enum OrderAction
{
    Confirm,
    MarkPaid,
    MarkReady,
    MarkShipped,
    Complete,
    Cancel,
}

/// <summary>What an admin changes in an order; <c>null</c> leaves a field as it is.</summary>
public sealed record OrderChanges(
    string? Title,
    string? Description,
    decimal? Price,
    Guid? ReferenceImageId,
    bool ClearReferenceImage,
    string? AdminNote);

/// <summary>Error codes of the order endpoints beyond the domain rules (<c>OrderErrors</c>).</summary>
public static class OrderApiErrors
{
    public const string NotFound = "orders.not_found";
    public const string Conflict = "orders.conflict";
    public const string ConversationNotFound = "orders.conversation_not_found";
    public const string ImageUnavailable = "orders.image_unavailable";
}
