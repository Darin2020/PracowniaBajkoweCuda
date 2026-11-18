using BajkoweCuda.Api.Auth;
using BajkoweCuda.Api.Chat;
using BajkoweCuda.Api.Problems;
using BajkoweCuda.Api.Security;
using BajkoweCuda.Domain;
using BajkoweCuda.Domain.Chat;
using BajkoweCuda.Infrastructure.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BajkoweCuda.Api.Admin;

/// <summary>The studio's side of the chat (docs/PLAN.md §3.4, §6, <c>/api/admin/conversations</c>).</summary>
[ApiController]
[Route("api/admin/conversations")]
[Authorize(Policy = AuthSetup.AdminPolicy)]
[EnableRateLimiting(RateLimiting.Admin)]
public sealed class AdminConversationsController(UserManager<Infrastructure.Identity.ApplicationUser> users, ChatService chat, ChatEvents events)
    : ControllerBase
{
    public const int DefaultPageSize = 30;

    /// <summary>Unread first, then the newest; <paramref name="search"/> matches the customer's name or email.</summary>
    [HttpGet]
    public Task<ConversationPage> List(string? search, string? cursor, int take = DefaultPageSize, CancellationToken cancellationToken = default) =>
        chat.ListAsync(search, cursor, take, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        await chat.GetConversationAsync(id, cancellationToken) is { } conversation
            ? Ok(conversation)
            : ChatApiErrors.ConversationNotFound.ToResult();

    [HttpGet("{id:guid}/messages")]
    public async Task<IActionResult> Messages(Guid id, Guid? before, int take = ChatService.DefaultPageSize, CancellationToken cancellationToken = default) =>
        await chat.FindCustomerIdAsync(id, cancellationToken) is null
            ? ChatApiErrors.ConversationNotFound.ToResult()
            : Ok(await chat.PageAsync(id, before, take, cancellationToken));

    [HttpPost("{id:guid}/messages")]
    public async Task<IActionResult> Post(Guid id, PostMessageRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(users.GetUserId(User), out var adminId))
        {
            return AuthErrors.NotAuthenticated.ToResult();
        }

        ChatMessage? message;
        try
        {
            message = await chat.PostAsStudioAsync(id, adminId, request.Body, request.AttachmentIds, cancellationToken);
        }
        catch (DomainException error)
        {
            return error.ToResult();
        }

        if (message is null)
        {
            return ChatApiErrors.ConversationNotFound.ToResult();
        }

        var customerId = (await chat.FindCustomerIdAsync(id, cancellationToken))!.Value;
        await events.MessagePostedAsync(customerId, message, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, message);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, MarkReadRequest request, CancellationToken cancellationToken)
    {
        var receipt = await chat.MarkReadAsync(id, ChatSide.Studio, request.UpToMessageId!.Value, cancellationToken);
        if (receipt is null)
        {
            return ChatApiErrors.MessageNotFound.ToResult();
        }

        await events.MessagesReadAsync(receipt, cancellationToken);
        return NoContent();
    }
}
