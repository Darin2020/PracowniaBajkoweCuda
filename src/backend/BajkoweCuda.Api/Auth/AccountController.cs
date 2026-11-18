using System.Globalization;
using System.Text.Json;
using BajkoweCuda.Api.Problems;
using BajkoweCuda.Api.Security;
using BajkoweCuda.Application.Audit;
using BajkoweCuda.Application.Emails;
using BajkoweCuda.Infrastructure.Accounts;
using BajkoweCuda.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BajkoweCuda.Api.Auth;

/// <summary>
/// The signed-in user's own account (docs/PLAN.md §3.2, §6). Every action works on the current user only;
/// there is no user id in the route.
/// </summary>
[ApiController]
[Route("api/account")]
public sealed partial class AccountController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    AccountData accountData,
    IEmailOutbox emails,
    IAuditLog audit,
    TimeProvider time,
    ILogger<AccountController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = await users.GetUserAsync(User);
        return user is null || user.IsDeleted ? AuthErrors.NotAuthenticated.ToResult() : Ok(ToResponse(user));
    }

    [HttpPatch]
    public async Task<IActionResult> Update(UpdateAccountRequest request)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeleted)
        {
            return AuthErrors.NotAuthenticated.ToResult();
        }

        if (request.DisplayName is { } displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                ModelState.AddModelError(nameof(request.DisplayName), "The display name cannot be empty.");
                return ValidationProblem(ModelState);
            }

            user.DisplayName = displayName.Trim();
        }

        if (request.PreferredLanguage is { } language)
        {
            user.PreferredLanguage = language;
        }

        var result = await users.UpdateAsync(user);
        return result.Succeeded
            ? Ok(ToResponse(user))
            : throw new InvalidOperationException($"Updating the account failed: {string.Join(", ", result.Errors.Select(error => error.Code))}");
    }

    /// <summary>"Export my data" (§3.2): a JSON download of everything stored about the user.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeleted)
        {
            return AuthErrors.NotAuthenticated.ToResult();
        }

        var export = await accountData.ExportAsync(user, cancellationToken);
        await audit.WriteAsync(AuditActions.AccountExported, user.Id, cancellationToken: cancellationToken);

        Response.Headers.CacheControl = "no-store";
        var fileName = string.Create(CultureInfo.InvariantCulture, $"bajkowecuda-account-{time.GetUtcNow():yyyy-MM-dd}.json");
        return File(JsonSerializer.SerializeToUtf8Bytes(export, ExportJson), "application/json", fileName);
    }

    /// <summary>Changes the password; the current one is asked again (§8.1). Other sessions end, this one stays.</summary>
    [HttpPost("password")]
    [EnableRateLimiting(RateLimiting.Auth)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeleted)
        {
            return AuthErrors.NotAuthenticated.ToResult();
        }

        if (!await ReauthenticateAsync(user, request.CurrentPassword, cancellationToken))
        {
            return AuthErrors.ReauthenticationFailed.ToResult();
        }

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return AuthErrors.PasswordRejected(result.Errors).ToResult();
        }

        // The new security stamp ends the other sessions at their next check; this one is re-issued.
        await signIn.RefreshSignInAsync(user);
        await emails.EnqueueAsync(EmailTemplate.PasswordChanged, user.Id, user.Email!, user.PreferredLanguage, new Dictionary<string, string>(), cancellationToken);
        await audit.WriteAsync(AuditActions.PasswordChanged, user.Id, cancellationToken: cancellationToken);
        LogPasswordChanged(logger, user.Id);
        return NoContent();
    }

    /// <summary>
    /// Deletes the account after asking for the password again (§8.6): erases the personal data, ends every session,
    /// confirms by email and writes an audit entry. Accounts without a password (external sign-in only) will
    /// re-authenticate with their provider; that comes with external sign-in.
    /// </summary>
    [HttpPost("delete")]
    [EnableRateLimiting(RateLimiting.Auth)]
    public async Task<IActionResult> Delete(DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeleted)
        {
            return AuthErrors.NotAuthenticated.ToResult();
        }

        if (!await ReauthenticateAsync(user, request.Password, cancellationToken))
        {
            return AuthErrors.ReauthenticationFailed.ToResult();
        }

        var email = user.Email;
        var language = user.PreferredLanguage;

        await audit.WriteAsync(AuditActions.AccountDeleted, user.Id, cancellationToken: cancellationToken);
        await accountData.EraseAsync(user, cancellationToken);

        // Not linked to the user (ToUserId null): this row is the only place the address remains, and the outbox
        // deletes finished rows after its retention period.
        if (email is not null)
        {
            await emails.EnqueueAsync(EmailTemplate.AccountDeleted, toUserId: null, email, language, new Dictionary<string, string>(), cancellationToken);
        }

        await signIn.SignOutAsync();
        HttpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
        HttpContext.IssueRequestToken();
        LogDeleted(logger, user.Id);
        return NoContent();
    }

    /// <summary>Checks the password again. Wrong guesses count towards the lockout, like failed sign-ins.</summary>
    private async Task<bool> ReauthenticateAsync(ApplicationUser user, string password, CancellationToken cancellationToken)
    {
        if (await users.HasPasswordAsync(user)
            && (await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)).Succeeded)
        {
            return true;
        }

        await audit.WriteAsync(AuditActions.ReauthenticationFailed, user.Id, cancellationToken: cancellationToken);
        return false;
    }

    private static AccountResponse ToResponse(ApplicationUser user) => new(
        user.Id,
        user.Email!,
        user.DisplayName,
        user.EmailConfirmed,
        user.PreferredLanguage,
        user.CreatedAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} changed the password")]
    private static partial void LogPasswordChanged(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} deleted the account")]
    private static partial void LogDeleted(ILogger logger, Guid userId);
}
