using BajkoweCuda.Api.Problems;
using BajkoweCuda.Application.Emails;
using BajkoweCuda.Infrastructure.Emails;
using BajkoweCuda.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BajkoweCuda.Api.Auth;

/// <summary>
/// Registration, sign-in and password recovery (docs/PLAN.md §3.2, §6). Answers never reveal whether an email
/// address has an account: register, resend and forgot-password always return 202, and every failed sign-in is
/// <c>auth.invalid_credentials</c> (§8.1).
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed partial class AuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IEmailOutbox emails,
    AccountLinks links,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>Used when the email is unknown, so a failed sign-in costs the same time either way.</summary>
    private static readonly Lazy<(ApplicationUser User, string Hash)> DummyPassword = new(() =>
    {
        var user = new ApplicationUser();
        return (user, new PasswordHasher<ApplicationUser>().HashPassword(user, Guid.NewGuid().ToString()));
    });

    /// <summary>The visitor's session. Never touches the database for anonymous visitors. Also issues the XSRF cookie.</summary>
    [HttpGet("me")]
    [AllowAnonymous]
    public async Task<MeResponse> Me()
    {
        HttpContext.IssueRequestToken();
        if (User.Identity?.IsAuthenticated != true)
        {
            return MeResponse.Anonymous;
        }

        var user = await users.GetUserAsync(User);
        return user is null || user.IsDeleted ? MeResponse.Anonymous : await ToMeAsync(user);
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        // The password is checked first, against a blank user: for an address that already has an account the
        // answer must be the same as for a new one.
        var passwordErrors = await ValidatePasswordAsync(request.Password);
        if (passwordErrors.Count > 0)
        {
            return AuthErrors.PasswordRejected(passwordErrors).ToResult();
        }

        var email = request.Email.Trim();
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            await NotifyExistingAccountAsync(existing, cancellationToken);
            return Accepted();
        }

        var now = time.GetUtcNow();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            PreferredLanguage = request.PreferredLanguage,
            CreatedAt = now,
            TermsAcceptedAt = now,
            TermsVersion = configuration["Legal:TermsVersion"],
        };

        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            // A parallel registration with the same address won the race: treat it as an existing account.
            if (await users.FindByEmailAsync(email) is { } raced)
            {
                await NotifyExistingAccountAsync(raced, cancellationToken);
                return Accepted();
            }

            return AuthErrors.PasswordRejected(created.Errors).ToResult();
        }

        LogRegistered(logger, user.Id);
        await SendConfirmationAsync(user, cancellationToken);
        return Accepted();
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || user.IsDeleted)
        {
            var (dummyUser, dummyHash) = DummyPassword.Value;
            users.PasswordHasher.VerifyHashedPassword(dummyUser, dummyHash, request.Password);
            return AuthErrors.InvalidCredentials.ToResult();
        }

        // Persistent cookie: the session survives closing the browser and slides for 14 days (§8.1).
        var result = await signIn.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: true);
        if (result.RequiresTwoFactor)
        {
            return Ok(new TwoFactorRequiredResponse());
        }

        if (!result.Succeeded)
        {
            LogSignInFailed(logger, user.Id, result.IsLockedOut);
            return AuthErrors.InvalidCredentials.ToResult();
        }

        // The new antiforgery token must be bound to the signed-in user.
        HttpContext.User = await signIn.CreateUserPrincipalAsync(user);
        HttpContext.IssueRequestToken();
        LogSignedIn(logger, user.Id);
        return Ok(await ToMeAsync(user));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        HttpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
        HttpContext.IssueRequestToken();
        return NoContent();
    }

    [HttpPost("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        var token = AccountLinks.DecodeToken(request.Code);
        if (user is null || user.IsDeleted || token is null)
        {
            return AuthErrors.InvalidToken.ToResult();
        }

        if (user.EmailConfirmed)
        {
            return NoContent();
        }

        var result = await users.ConfirmEmailAsync(user, token);
        return result.Succeeded ? NoContent() : AuthErrors.InvalidToken.ToResult();
    }

    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendConfirmation(EmailRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is { EmailConfirmed: false, IsDeleted: false })
        {
            await SendConfirmationAsync(user, cancellationToken);
        }

        return Accepted();
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is { IsDeleted: false })
        {
            if (user.EmailConfirmed)
            {
                var token = await users.GeneratePasswordResetTokenAsync(user);
                await EnqueueAsync(user, EmailTemplate.ResetPassword, links.ResetPassword(user.Id, token, user.PreferredLanguage), cancellationToken);
            }
            else
            {
                // An unconfirmed address cannot prove ownership through a reset link; confirming it comes first.
                await SendConfirmationAsync(user, cancellationToken);
            }
        }

        return Accepted();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        var token = AccountLinks.DecodeToken(request.Code);
        if (user is null || user.IsDeleted || token is null)
        {
            return AuthErrors.InvalidToken.ToResult();
        }

        var result = await users.ResetPasswordAsync(user, token, request.Password);
        if (!result.Succeeded)
        {
            var invalidToken = result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.InvalidToken));
            return invalidToken ? AuthErrors.InvalidToken.ToResult() : AuthErrors.PasswordRejected(result.Errors).ToResult();
        }

        // Resetting changes the security stamp, which ends the other sessions; a lockout from guessing ends too.
        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        await EnqueueAsync(user, EmailTemplate.PasswordChanged, link: null, cancellationToken);
        LogPasswordReset(logger, user.Id);
        return NoContent();
    }

    private async Task<MeResponse> ToMeAsync(ApplicationUser user) => new(
        Authenticated: true,
        Id: user.Id,
        Email: user.Email,
        DisplayName: user.DisplayName,
        EmailConfirmed: user.EmailConfirmed,
        PreferredLanguage: user.PreferredLanguage,
        Roles: [.. await users.GetRolesAsync(user)]);

    private async Task<List<IdentityError>> ValidatePasswordAsync(string password)
    {
        var errors = new List<IdentityError>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, new ApplicationUser(), password);
            errors.AddRange(result.Errors);
        }

        return errors;
    }

    private async Task NotifyExistingAccountAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (user.IsDeleted)
        {
            return;
        }

        if (user.EmailConfirmed)
        {
            await EnqueueAsync(user, EmailTemplate.AlreadyRegistered, links.ForgotPassword(user.PreferredLanguage), cancellationToken);
        }
        else
        {
            await SendConfirmationAsync(user, cancellationToken);
        }
    }

    private async Task SendConfirmationAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        await EnqueueAsync(user, EmailTemplate.ConfirmEmail, links.ConfirmEmail(user.Id, token, user.PreferredLanguage), cancellationToken);
    }

    private Task EnqueueAsync(ApplicationUser user, EmailTemplate template, string? link, CancellationToken cancellationToken)
    {
        var payload = link is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { [EmailRenderer.LinkKey] = link };

        return emails.EnqueueAsync(template, user.Id, user.Email!, user.PreferredLanguage, payload, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} registered")]
    private static partial void LogRegistered(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} signed in")]
    private static partial void LogSignedIn(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in failed for user {UserId} (locked out: {LockedOut})")]
    private static partial void LogSignInFailed(ILogger logger, Guid userId, bool lockedOut);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} reset the password")]
    private static partial void LogPasswordReset(ILogger logger, Guid userId);
}
