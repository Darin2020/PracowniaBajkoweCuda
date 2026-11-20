using BajkoweCuda.Api.Auth;
using BajkoweCuda.Api.Chat;
using BajkoweCuda.Api.Problems;
using BajkoweCuda.Api.Security;
using BajkoweCuda.Application.Common;
using BajkoweCuda.Application.Files;
using BajkoweCuda.Domain;
using BajkoweCuda.Domain.Orders;
using BajkoweCuda.Infrastructure.Chat;
using BajkoweCuda.Infrastructure.Imaging;
using BajkoweCuda.Api.Orders;
using BajkoweCuda.Infrastructure.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BajkoweCuda.Api.Admin;

/// <summary>
/// The studio's orders (docs/PLAN.md §3.5, §6, <c>/api/admin/orders</c>, Admin policy). Changes that would overwrite
/// another admin's change answer <c>409 orders.conflict</c> (the request carries the <c>rowVersion</c> it saw).
/// </summary>
[ApiController]
[Route("api/admin/orders")]
[Authorize(Policy = AuthSetup.AdminPolicy)]
[EnableRateLimiting(RateLimiting.Admin)]
public sealed class AdminOrdersController(
    UserManager<Infrastructure.Identity.ApplicationUser> users,
    OrderService orders,
    Attachments attachments,
    ChatEvents events) : ControllerBase
{
    private const long PhotoRequestLimit = ImageProcessor.MaxBytes + (512 * 1024);

    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest request, CancellationToken cancellationToken) =>
        await RunAsync(adminId => orders.CreateAsync(
            adminId,
            request.ConversationId!.Value,
            request.Title,
            request.Description,
            request.Price!.Value,
            request.ReferenceAttachmentId,
            request.AdminNote,
            cancellationToken), created: true, cancellationToken);

    /// <summary>Newest first; <paramref name="status"/> filters (Draft, AwaitingPayment, ...), <paramref name="conversationId"/> gives one customer's orders.</summary>
    [HttpGet]
    public async Task<IActionResult> List(string? status, Guid? conversationId, string? cursor, int take = OrderService.DefaultPageSize, CancellationToken cancellationToken = default)
    {
        OrderStatus? filter = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return new AppError("orders.invalid_status", ErrorKind.Validation).ToResult();
            }

            filter = parsed;
        }

        return Ok(await orders.ListAsync(filter, conversationId, cursor, take, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        await orders.GetForAdminAsync(id, cancellationToken) is { } order ? Ok(order) : NotFound(OrderApiErrors.NotFound);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateOrderRequest request, CancellationToken cancellationToken)
    {
        if (RowVersionOf(request.RowVersion) is not { } rowVersion)
        {
            return new AppError("orders.row_version_required", ErrorKind.Validation).ToResult();
        }

        var changes = new OrderChanges(request.Title, request.Description, request.Price, request.ReferenceAttachmentId, request.ClearReferenceImage, request.AdminNote);
        return await RunAsync(adminId => orders.UpdateAsync(id, adminId, changes, rowVersion, cancellationToken), created: false, cancellationToken);
    }

    [HttpPost("{id:guid}/confirm")]
    public Task<IActionResult> Confirm(Guid id, StepRequest? request, CancellationToken cancellationToken) => StepAsync(id, OrderAction.Confirm, request, cancellationToken);

    [HttpPost("{id:guid}/mark-paid")]
    public Task<IActionResult> MarkPaid(Guid id, StepRequest? request, CancellationToken cancellationToken) => StepAsync(id, OrderAction.MarkPaid, request, cancellationToken);

    [HttpPost("{id:guid}/mark-ready")]
    public Task<IActionResult> MarkReady(Guid id, StepRequest? request, CancellationToken cancellationToken) => StepAsync(id, OrderAction.MarkReady, request, cancellationToken);

    [HttpPost("{id:guid}/mark-shipped")]
    public Task<IActionResult> MarkShipped(Guid id, StepRequest? request, CancellationToken cancellationToken) => StepAsync(id, OrderAction.MarkShipped, request, cancellationToken);

    [HttpPost("{id:guid}/complete")]
    public Task<IActionResult> Complete(Guid id, StepRequest? request, CancellationToken cancellationToken) => StepAsync(id, OrderAction.Complete, request, cancellationToken);

    [HttpPost("{id:guid}/cancel")]
    public Task<IActionResult> Cancel(Guid id, StepRequest? request, CancellationToken cancellationToken) => StepAsync(id, OrderAction.Cancel, request, cancellationToken);

    /// <summary>A photo of the finished toy, as multipart field <c>file</c>; re-encoded like chat images.</summary>
    [HttpPost("{id:guid}/photos")]
    [RequestSizeLimit(PhotoRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = PhotoRequestLimit)]
    public async Task<IActionResult> AddPhoto(Guid id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(users.GetUserId(User), out var adminId))
        {
            return AuthErrors.NotAuthenticated.ToResult();
        }

        if (await orders.GetForAdminAsync(id, cancellationToken) is null)
        {
            return NotFound(OrderApiErrors.NotFound);
        }

        var file = Request.HasFormContentType ? (await Request.ReadFormAsync(cancellationToken)).Files.GetFile("file") : null;
        if (file is null || file.Length == 0)
        {
            return ChatApiErrors.FileMissing.ToResult();
        }

        if (file.Length > ImageProcessor.MaxBytes)
        {
            return new AppError(ImageErrors.TooLarge, ErrorKind.Validation).ToResult();
        }

        var content = new byte[file.Length];
        await using (var stream = file.OpenReadStream())
        {
            await stream.ReadExactlyAsync(content, cancellationToken);
        }

        var (upload, error) = await attachments.UploadAsync(adminId, content, cancellationToken, FileContainers.OrderPhotos);
        if (upload is null)
        {
            return new AppError(error!, ErrorKind.Validation).ToResult();
        }

        // A refused photo (wrong status) leaves an unused upload, which the daily cleanup removes.
        return await RunAsync(_ => orders.AddPhotoAsync(id, adminId, upload, cancellationToken), created: true, cancellationToken);
    }

    [HttpDelete("{id:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> RemovePhoto(Guid id, Guid photoId, CancellationToken cancellationToken) =>
        await RunAsync(adminId => orders.RemovePhotoAsync(id, adminId, photoId, cancellationToken), created: false, cancellationToken);

    [HttpPut("{id:guid}/photos/order")]
    public async Task<IActionResult> ReorderPhotos(Guid id, ReorderPhotosRequest request, CancellationToken cancellationToken) =>
        await RunAsync(_ => orders.ReorderPhotosAsync(id, request.PhotoIds, cancellationToken), created: false, cancellationToken);

    private Task<IActionResult> StepAsync(Guid id, OrderAction action, StepRequest? request, CancellationToken cancellationToken)
    {
        var rowVersion = request?.RowVersion is null ? null : RowVersionOf(request.RowVersion);
        return RunAsync(adminId => orders.ApplyAsync(id, adminId, action, request?.Reason, rowVersion, cancellationToken), created: false, cancellationToken);
    }

    /// <summary>Runs a change, maps the domain's refusals to problem details, and pushes the events of a success.</summary>
    private async Task<IActionResult> RunAsync(Func<Guid, Task<OrderChange?>> change, bool created, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(users.GetUserId(User), out var adminId))
        {
            return AuthErrors.NotAuthenticated.ToResult();
        }

        OrderChange? result;
        try
        {
            result = await change(adminId);
        }
        catch (DomainException error)
        {
            return OrderProblems.From(error).ToResult();
        }
        catch (DbUpdateConcurrencyException)
        {
            return new AppError(OrderApiErrors.Conflict, ErrorKind.Conflict).ToResult();
        }

        if (result is null)
        {
            return NotFound(OrderApiErrors.NotFound);
        }

        await events.OrderChangedAsync(result, cancellationToken);
        return created ? StatusCode(StatusCodes.Status201Created, result.Order) : Ok(result.Order);
    }

    private static ObjectResult NotFound(string code) => new AppError(code, ErrorKind.NotFound).ToResult();

    private static byte[]? RowVersionOf(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

internal static class OrderProblems
{
    /// <summary>Steps and edits that the current status does not allow are conflicts (409); bad input is 400; a missing conversation is 404.</summary>
    public static AppError From(DomainException error) => error.Code switch
    {
        OrderErrors.InvalidTransition or OrderErrors.NotEditable or OrderErrors.PhotoRequired or OrderErrors.PhotosLocked
            => new AppError(error.Code, ErrorKind.Conflict),
        OrderApiErrors.ConversationNotFound or OrderErrors.PhotoNotFound => new AppError(error.Code, ErrorKind.NotFound),
        _ => new AppError(error.Code, ErrorKind.Validation),
    };
}
