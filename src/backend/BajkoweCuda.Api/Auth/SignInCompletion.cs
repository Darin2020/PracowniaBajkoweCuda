using BajkoweCuda.Infrastructure.Chat;
using BajkoweCuda.Infrastructure.Identity;
using BajkoweCuda.Infrastructure.Orders;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BajkoweCuda.Api.Auth;

internal static partial class SignInCompletion
{
    /// <summary>
    /// After the session cookie is issued: binds a new antiforgery token to the signed-in user (a token belongs to the
    /// user it was issued for) and returns the session summary, with the chat badge.
    /// </summary>
    public static async Task<MeResponse> CompleteSignInAsync(
        this ControllerBase controller,
        SignInManager<ApplicationUser> signIn,
        UserManager<ApplicationUser> users,
        ApplicationUser user,
        ILogger logger)
    {
        controller.HttpContext.User = await signIn.CreateUserPrincipalAsync(user);
        controller.HttpContext.IssueRequestToken();
        LogSignedIn(logger, user.Id);
        var unread = await UnreadCountAsync(controller.HttpContext, user);
        return await MeResponse.ForAsync(users, user, unread, await HasOrdersAsync(controller.HttpContext, user));
    }

    /// <summary>Whether the "My orders" tab is shown (§3.5): the customer has an order that is not a draft.</summary>
    public static Task<bool> HasOrdersAsync(HttpContext context, ApplicationUser user) =>
        context.RequestServices.GetRequiredService<OrderService>().CustomerHasOrdersAsync(user.Id, context.RequestAborted);

    /// <summary>
    /// The chat badge for the signed-in user (also <c>GET /api/auth/me</c>): the customer's unread studio messages, or for
    /// an admin whose session has the second factor all unread customer messages.
    /// </summary>
    public static async Task<int> UnreadCountAsync(HttpContext context, ApplicationUser user)
    {
        var chat = context.RequestServices.GetRequiredService<ChatService>();
        if (context.User.IsInRole(Roles.Admin))
        {
            return context.User.HasMultiFactor() ? await chat.StudioUnreadAsync(context.RequestAborted) : 0;
        }

        return await chat.CustomerUnreadAsync(user.Id, context.RequestAborted);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} signed in")]
    private static partial void LogSignedIn(ILogger logger, Guid userId);
}
