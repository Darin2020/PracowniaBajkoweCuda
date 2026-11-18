using BajkoweCuda.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BajkoweCuda.Api.Auth;

internal static partial class SignInCompletion
{
    /// <summary>
    /// After the session cookie is issued: binds a new antiforgery token to the signed-in user (a token belongs to the
    /// user it was issued for) and returns the session summary.
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
        return await MeResponse.ForAsync(users, user);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} signed in")]
    private static partial void LogSignedIn(ILogger logger, Guid userId);
}
