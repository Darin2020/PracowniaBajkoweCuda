using BajkoweCuda.Api.Auth;
using BajkoweCuda.Infrastructure.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BajkoweCuda.Api.Chat;

/// <summary>
/// The events the server pushes (docs/PLAN.md §6, SignalR). There are no client-to-server methods: sending goes
/// through REST, so validation, rate limits and antiforgery apply to it.
/// </summary>
public interface IChatClient
{
    Task MessageReceived(ChatMessage message);

    Task MessagesRead(ReadReceipt receipt);

    Task UnreadCountChanged(int count);
}

/// <summary>
/// <c>/hubs/chat</c>, same origin, with the session cookie. A customer's connections are addressed by their user id
/// (SignalR's user identifier is the NameIdentifier claim); admins whose session meets the admin policy (2FA) join
/// the <c>admins</c> group and hear every conversation.
/// </summary>
[Authorize]
public sealed class ChatHub(IAuthorizationService authorization) : Hub<IChatClient>
{
    public const string Path = "/hubs/chat";
    public const string AdminsGroup = "admins";

    public override async Task OnConnectedAsync()
    {
        if (Context.User is { } user && (await authorization.AuthorizeAsync(user, AuthSetup.AdminPolicy)).Succeeded)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
        }

        await base.OnConnectedAsync();
    }
}

/// <summary>Pushes chat events after the database has changed.</summary>
public sealed class ChatEvents(IHubContext<ChatHub, IChatClient> hub, ChatService chat)
{
    /// <summary>A new message: to the conversation's customer and to the admins.</summary>
    public async Task MessagePostedAsync(Guid customerId, ChatMessage message, CancellationToken cancellationToken)
    {
        await hub.Clients.User(customerId.ToString()).MessageReceived(message);
        await hub.Clients.Group(ChatHub.AdminsGroup).MessageReceived(message);
        if (message.From == "studio")
        {
            await hub.Clients.User(customerId.ToString()).UnreadCountChanged(await chat.CustomerUnreadAsync(customerId, cancellationToken));
        }
        else
        {
            await hub.Clients.Group(ChatHub.AdminsGroup).UnreadCountChanged(await chat.StudioUnreadAsync(cancellationToken));
        }
    }

    /// <summary>One side has read: both sides update their ticks, the reader's badge goes down.</summary>
    public async Task MessagesReadAsync(ReadReceipt receipt, CancellationToken cancellationToken)
    {
        await hub.Clients.User(receipt.CustomerId.ToString()).MessagesRead(receipt);
        await hub.Clients.Group(ChatHub.AdminsGroup).MessagesRead(receipt);
        if (receipt.By == "customer")
        {
            await hub.Clients.User(receipt.CustomerId.ToString()).UnreadCountChanged(await chat.CustomerUnreadAsync(receipt.CustomerId, cancellationToken));
        }
        else
        {
            await hub.Clients.Group(ChatHub.AdminsGroup).UnreadCountChanged(await chat.StudioUnreadAsync(cancellationToken));
        }
    }
}
