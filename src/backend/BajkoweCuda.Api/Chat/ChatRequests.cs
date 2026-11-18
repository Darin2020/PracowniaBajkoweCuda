using System.ComponentModel.DataAnnotations;
using BajkoweCuda.Api.Problems;
using BajkoweCuda.Application.Common;
using BajkoweCuda.Domain;
using Microsoft.AspNetCore.Mvc;

namespace BajkoweCuda.Api.Chat;

/// <summary>A new message: text and/or the ids of images uploaded before (<c>POST /api/chat/attachments</c>).</summary>
public sealed class PostMessageRequest
{
    /// <summary>The domain checks the trimmed length (2,000); this only bounds the request.</summary>
    [MaxLength(4000)]
    public string? Body { get; init; }

    [MaxLength(10)]
    public IReadOnlyList<Guid> AttachmentIds { get; init; } = [];
}

public sealed class MarkReadRequest
{
    [Required]
    public Guid? UpToMessageId { get; init; }
}

public sealed class EmailNotificationsRequest
{
    [Required]
    public bool? Enabled { get; init; }
}

internal static class DomainProblems
{
    /// <summary>A broken chat rule is the client's mistake: 400 with the rule's code.</summary>
    public static ObjectResult ToResult(this DomainException error) => new AppError(error.Code, ErrorKind.Validation).ToResult();
}
