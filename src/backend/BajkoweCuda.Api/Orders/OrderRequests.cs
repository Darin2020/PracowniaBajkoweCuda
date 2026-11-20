using System.ComponentModel.DataAnnotations;
using BajkoweCuda.Domain.Orders;

namespace BajkoweCuda.Api.Orders;

// Request shapes of /api/admin/orders. The domain checks the rules (lengths, price, status); these only bound the input.

public sealed class CreateOrderRequest
{
    [Required]
    public Guid? ConversationId { get; init; }

    [Required, MaxLength(Order.TitleMaxLength + 100)]
    public string Title { get; init; } = string.Empty;

    [MaxLength(Order.DescriptionMaxLength + 100)]
    public string? Description { get; init; }

    [Required]
    public decimal? Price { get; init; }

    /// <summary>An image of the conversation, or a fresh upload of the admin (<c>POST /api/chat/attachments</c>).</summary>
    public Guid? ReferenceAttachmentId { get; init; }

    [MaxLength(Order.AdminNoteMaxLength + 100)]
    public string? AdminNote { get; init; }
}

/// <summary>Only the fields that are sent change. <see cref="RowVersion"/> is the order's version the admin saw.</summary>
public sealed class UpdateOrderRequest
{
    [MaxLength(Order.TitleMaxLength + 100)]
    public string? Title { get; init; }

    [MaxLength(Order.DescriptionMaxLength + 100)]
    public string? Description { get; init; }

    public decimal? Price { get; init; }

    public Guid? ReferenceAttachmentId { get; init; }

    /// <summary>Removes the reference image.</summary>
    public bool ClearReferenceImage { get; init; }

    [MaxLength(Order.AdminNoteMaxLength + 100)]
    public string? AdminNote { get; init; }

    [Required, MaxLength(64)]
    public string? RowVersion { get; init; }
}

/// <summary>A step of the order: the cancellation reason, and optionally the version the admin saw.</summary>
public sealed class StepRequest
{
    [MaxLength(Order.ReasonMaxLength + 100)]
    public string? Reason { get; init; }

    [MaxLength(64)]
    public string? RowVersion { get; init; }
}

public sealed class ReorderPhotosRequest
{
    [Required, MaxLength(Order.MaxPhotos)]
    public IReadOnlyList<Guid> PhotoIds { get; init; } = [];
}
