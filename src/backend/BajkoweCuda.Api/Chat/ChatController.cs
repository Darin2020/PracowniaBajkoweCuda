using BajkoweCuda.Api.Auth;
using BajkoweCuda.Api.Problems;
using BajkoweCuda.Api.Security;
using BajkoweCuda.Domain;
using BajkoweCuda.Domain.Chat;
using BajkoweCuda.Infrastructure.Chat;
using BajkoweCuda.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BajkoweCuda.Api.Chat;

/// <summary>
/// The customer's chat (docs/PLAN.md §3.4, §6, <c>/api/chat</c>). Every action works on the signed-in user's own
/// conversation, found by their user id: there is no conversation id to tamper with.
/// </summary>
[ApiController]
[Route("api/chat")]
public sealed class ChatController(UserManager<ApplicationUser> users, ChatService chat, ChatEvents events) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var (user, problem) = await ChatUserAsync();
        return problem ?? Ok(await chat.GetCustomerChatAsync(user!.Id, cancellationToken));
    }

    /// <summary>Newest first. Empty until the first message.</summary>
    [HttpGet("messages")]
    public async Task<IActionResult> Messages(Guid? before, int take = ChatService.DefaultPageSize, CancellationToken cancellationToken = default)
    {
        var (user, problem) = await ChatUserAsync();
        if (problem is not null)
        {
            return problem;
        }

        return await chat.FindConversationIdAsync(user!.Id, cancellationToken) is { } conversationId
            ? Ok(await chat.PageAsync(conversationId, before, take, cancellationToken))
            : Ok(new ChatPage([], null));
    }

    [HttpPost("messages")]
    [EnableRateLimiting(RateLimiting.Chat)]
    public async Task<IActionResult> Post(PostMessageRequest request, CancellationToken cancellationToken)
    {
        var (user, problem) = await ChatUserAsync();
        if (problem is not null)
        {
            return problem;
        }

        ChatMessage message;
        try
        {
            (message, _) = await chat.PostAsCustomerAsync(user!.Id, request.Body, request.AttachmentIds, cancellationToken);
        }
        catch (DomainException error)
        {
            return error.ToResult();
        }

        await events.MessagePostedAsync(user.Id, message, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, message);
    }

    /// <summary>Marks the studio's messages read up to the given one (the newest the customer has seen).</summary>
    [HttpPost("read")]
    public async Task<IActionResult> Read(MarkReadRequest request, CancellationToken cancellationToken)
    {
        var (user, problem) = await ChatUserAsync();
        if (problem is not null)
        {
            return problem;
        }

        var receipt = await chat.FindConversationIdAsync(user!.Id, cancellationToken) is { } conversationId
            ? await chat.MarkReadAsync(conversationId, ChatSide.Customer, request.UpToMessageId!.Value, cancellationToken)
            : null;
        if (receipt is null)
        {
            return ChatApiErrors.MessageNotFound.ToResult();
        }

        await events.MessagesReadAsync(receipt, cancellationToken);
        return NoContent();
    }

    /// <summary>"Send replies to my email" (§3.6); also answers the question asked after the first message.</summary>
    [HttpPut("email-notifications")]
    public async Task<IActionResult> EmailNotifications(EmailNotificationsRequest request, CancellationToken cancellationToken)
    {
        var (user, problem) = await ChatUserAsync();
        if (problem is not null)
        {
            return problem;
        }

        return await chat.SetEmailNotificationsAsync(user!.Id, request.Enabled!.Value, cancellationToken)
            ? Ok(await chat.GetCustomerChatAsync(user.Id, cancellationToken))
            : ChatApiErrors.NotStarted.ToResult();
    }

    /// <summary>The signed-in user, who must have confirmed their address to chat (§3.2).</summary>
    private async Task<(ApplicationUser? User, IActionResult? Problem)> ChatUserAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeleted)
        {
            return (null, AuthErrors.NotAuthenticated.ToResult());
        }

        return user.EmailConfirmed ? (user, null) : (null, ChatApiErrors.EmailNotConfirmed.ToResult());
    }
}
