using System.Security.Claims;
using BajkoweCuda.Api.Problems;
using BajkoweCuda.Api.Security;
using BajkoweCuda.Application.Audit;
using BajkoweCuda.Application.Emails;
using BajkoweCuda.Infrastructure.Emails;
using BajkoweCuda.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BajkoweCuda.Api.Auth;

/// <summary>
/// Sign-in with an external provider (docs/PLAN.md §3.2, §6). The browser navigates (full page) to
/// <c>GET external/{provider}</c>, the provider sends it back to its callback path (<c>/signin-google</c>), and
/// <c>GET external/callback</c> finishes and redirects to a page of the Angular app. Three modes:
/// <list type="bullet">
/// <item><c>signin</c>: sign in, link by verified email, or continue to the complete-profile step for a new account;</item>
/// <item><c>link</c>: add the provider to the signed-in account;</item>
/// <item><c>reauth</c>: prove the signed-in user again before deleting an account that has no password (§8.6).</item>
/// </list>
/// Results are redirects with <c>?error=...</c> codes, because the browser, not the Angular app, follows them.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed partial class ExternalAuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    AdminRoleSync adminRoles,
    IEmailOutbox emails,
    EmailThrottle emailThrottle,
    AccountLinks links,
    IAuditLog audit,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<ExternalAuthController> logger) : ControllerBase
{
    private const string CallbackPath = "/api/auth/external/callback";
    private const string SignInMode = "signin";
    private const string LinkMode = "link";
    private const string ReauthMode = "reauth";

    /// <summary>The providers configured in this environment, for the sign-in buttons: <c>["google"]</c>.</summary>
    [HttpGet("providers")]
    [AllowAnonymous]
    public async Task<IReadOnlyList<string>> Providers() =>
        [.. (await signIn.GetExternalAuthenticationSchemesAsync()).Select(scheme => scheme.Name.ToLowerInvariant()).Order(StringComparer.Ordinal)];

    /// <summary>Starts the round trip to the provider. <paramref name="mode"/> link and reauth need a signed-in user.</summary>
    [HttpGet("external/{provider}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.Auth)]
    public async Task<IActionResult> Challenge(string provider, string? returnUrl, string? mode, string? lang)
    {
        var scheme = await FindSchemeAsync(provider);
        if (scheme is null)
        {
            return AuthErrors.ProviderUnavailable.ToResult();
        }

        var safeReturnUrl = ExternalProviders.SafeReturnUrl(returnUrl);
        var language = ExternalProviders.LanguageOf(safeReturnUrl) ?? (lang is null ? null : Domain.Languages.Find(lang));
        mode = mode is LinkMode or ReauthMode ? mode : SignInMode;

        string? userId = null;
        if (mode != SignInMode)
        {
            userId = users.GetUserId(User);
            if (userId is null)
            {
                return Redirect(ExternalProviders.FrontendUrl(language, "login"));
            }
        }

        // With a user id, Identity binds the provider's answer to this user: another session cannot use it.
        var properties = signIn.ConfigureExternalAuthenticationProperties(scheme, CallbackPath, userId);
        properties.Items[ExternalProviders.ModeItem] = mode;
        properties.Items[ExternalProviders.LanguageItem] = language;
        properties.Items[ExternalProviders.ReturnUrlItem] = safeReturnUrl;
        return new ChallengeResult(scheme, properties);
    }

    /// <summary>Where the provider's handler sends the browser after it has stored the answer in the external cookie.</summary>
    [HttpGet("external/callback")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.Auth)]
    public async Task<IActionResult> Callback(CancellationToken cancellationToken)
    {
        var external = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        var items = external.Properties?.Items ?? new Dictionary<string, string?>();
        var mode = items.TryGetValue(ExternalProviders.ModeItem, out var storedMode) ? storedMode : SignInMode;
        var language = items.TryGetValue(ExternalProviders.LanguageItem, out var storedLanguage) ? storedLanguage : null;
        var returnUrl = ExternalProviders.SafeReturnUrl(items.TryGetValue(ExternalProviders.ReturnUrlItem, out var storedReturnUrl) ? storedReturnUrl : null);

        var info = mode == SignInMode
            ? await signIn.GetExternalLoginInfoAsync()
            : await signIn.GetExternalLoginInfoAsync(users.GetUserId(User));
        if (info is null)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return Redirect(ExternalProviders.FrontendUrl(language, mode == SignInMode ? "login" : "account", "error=external_failed"));
        }

        return mode switch
        {
            LinkMode => await LinkAsync(info, language, cancellationToken),
            ReauthMode => await ReauthenticateAsync(info, language, returnUrl, cancellationToken),
            _ => await SignInAsync(info, language, returnUrl, cancellationToken),
        };
    }

    /// <summary>What the complete-profile page shows: the provider and what it told about the person.</summary>
    [HttpGet("complete-profile")]
    [AllowAnonymous]
    public async Task<IActionResult> PendingProfile()
    {
        var info = await signIn.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return AuthErrors.ExternalSessionExpired.ToResult();
        }

        return Ok(new PendingProfileResponse(
            info.LoginProvider.ToLowerInvariant(),
            info.Principal.FindFirstValue(ClaimTypes.Email),
            IsEmailVerified(info),
            info.Principal.FindFirstValue(ClaimTypes.Name)));
    }

    /// <summary>
    /// Creates the account for a new external sign-in: the terms must be accepted, and the email given when the
    /// provider sent none (§3.2). An address the provider did not verify gets a confirmation email.
    /// </summary>
    [HttpPost("complete-profile")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.Auth)]
    public async Task<IActionResult> CompleteProfile(CompleteProfileRequest request, CancellationToken cancellationToken)
    {
        var info = await signIn.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return AuthErrors.ExternalSessionExpired.ToResult();
        }

        var providerEmail = info.Principal.FindFirstValue(ClaimTypes.Email);
        var email = (providerEmail ?? request.Email)?.Trim();
        if (string.IsNullOrEmpty(email))
        {
            ModelState.AddModelError(nameof(request.Email), "The provider sent no email address: enter one.");
            return ValidationProblem(ModelState);
        }

        if (await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey) is not null || await users.FindByEmailAsync(email) is not null)
        {
            return AuthErrors.AccountExists.ToResult();
        }

        var now = time.GetUtcNow();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = providerEmail is not null && IsEmailVerified(info),
            DisplayName = request.DisplayName.Trim(),
            PreferredLanguage = request.PreferredLanguage,
            CreatedAt = now,
            TermsAcceptedAt = now,
            TermsVersion = configuration["Legal:TermsVersion"],
        };

        var created = await users.CreateAsync(user);
        if (!created.Succeeded)
        {
            // Two tabs finishing at once, or an address taken in between.
            return AuthErrors.AccountExists.ToResult();
        }

        Check(await users.AddLoginAsync(user, info));
        await audit.WriteAsync(AuditActions.Registered, user.Id, details: new { method = Method(info) }, cancellationToken: cancellationToken);
        LogRegistered(logger, user.Id, info.LoginProvider);

        if (!user.EmailConfirmed && emailThrottle.TryAcquire(email))
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            await emails.EnqueueAsync(
                EmailTemplate.ConfirmEmail,
                user.Id,
                email,
                user.PreferredLanguage,
                new Dictionary<string, string> { [EmailRenderer.LinkKey] = links.ConfirmEmail(user.Id, token, user.PreferredLanguage) },
                cancellationToken);
        }

        await adminRoles.SyncUserAsync(user, cancellationToken);
        await signIn.SignInAsync(user, isPersistent: true, info.LoginProvider);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return Ok(await this.CompleteSignInAsync(signIn, users, user, logger));
    }

    private async Task<IActionResult> SignInAsync(ExternalLoginInfo info, string? language, string? returnUrl, CancellationToken cancellationToken)
    {
        var user = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);

        if (user is null && info.Principal.FindFirstValue(ClaimTypes.Email) is { } email
            && await users.FindByEmailAsync(email) is { IsDeleted: false } existing)
        {
            // Link automatically only when the provider vouches for the address and the account confirmed it too;
            // anything else could hand an account to whoever controls the provider account (§3.2).
            if (!IsEmailVerified(info) || !existing.EmailConfirmed)
            {
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return Redirect(ExternalProviders.FrontendUrl(language, "login", "error=account_exists"));
            }

            Check(await users.AddLoginAsync(existing, info));
            await audit.WriteAsync(AuditActions.ExternalLoginLinked, existing.Id, details: new { method = Method(info), automatic = true }, cancellationToken: cancellationToken);
            user = existing;
        }

        if (user is null)
        {
            // A new person: the external cookie keeps the provider's answer for the complete-profile step.
            var query = returnUrl is null ? null : $"returnUrl={Uri.EscapeDataString(returnUrl)}";
            return Redirect(ExternalProviders.FrontendUrl(language, "complete-profile", query));
        }

        if (user.IsDeleted)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return Redirect(ExternalProviders.FrontendUrl(language, "login", "error=external_failed"));
        }

        await adminRoles.SyncUserAsync(user, cancellationToken);
        var result = await signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: false);
        language ??= user.PreferredLanguage;

        if (result.RequiresTwoFactor)
        {
            var query = returnUrl is null ? null : $"returnUrl={Uri.EscapeDataString(returnUrl)}";
            return Redirect(ExternalProviders.FrontendUrl(language, "login/2fa", query));
        }

        if (!result.Succeeded)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            await audit.WriteAsync(AuditActions.LoginFailed, user.Id, details: new { method = Method(info), lockedOut = result.IsLockedOut }, cancellationToken: cancellationToken);
            return Redirect(ExternalProviders.FrontendUrl(language, "login", "error=external_failed"));
        }

        await audit.WriteAsync(AuditActions.LoginSucceeded, user.Id, details: new { method = Method(info) }, cancellationToken: cancellationToken);
        LogSignedIn(logger, user.Id, info.LoginProvider);
        return Redirect(returnUrl ?? ExternalProviders.FrontendUrl(language, string.Empty));
    }

    private async Task<IActionResult> LinkAsync(ExternalLoginInfo info, string? language, CancellationToken cancellationToken)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeleted)
        {
            return Redirect(ExternalProviders.FrontendUrl(language, "login"));
        }

        language ??= user.PreferredLanguage;
        var owner = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (owner is not null && owner.Id != user.Id)
        {
            return Redirect(ExternalProviders.FrontendUrl(language, "account", "error=login_in_use"));
        }

        if (owner is null)
        {
            Check(await users.AddLoginAsync(user, info));
            // Adding a login changes the security stamp: keep this session valid.
            await signIn.RefreshSignInAsync(user);
            await audit.WriteAsync(AuditActions.ExternalLoginLinked, user.Id, details: new { method = Method(info) }, cancellationToken: cancellationToken);
        }

        return Redirect(ExternalProviders.FrontendUrl(language, "account", $"linked={Method(info)}"));
    }

    private async Task<IActionResult> ReauthenticateAsync(ExternalLoginInfo info, string? language, string? returnUrl, CancellationToken cancellationToken)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeleted)
        {
            return Redirect(ExternalProviders.FrontendUrl(language, "login"));
        }

        language ??= user.PreferredLanguage;
        var owner = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (owner?.Id != user.Id)
        {
            await audit.WriteAsync(AuditActions.ReauthenticationFailed, user.Id, details: new { method = Method(info) }, cancellationToken: cancellationToken);
            return Redirect(ExternalProviders.FrontendUrl(language, "account", "error=reauthentication_failed"));
        }

        await HttpContext.MarkReauthenticatedAsync(signIn, user, time.GetUtcNow());
        await audit.WriteAsync(AuditActions.Reauthenticated, user.Id, details: new { method = Method(info) }, cancellationToken: cancellationToken);
        return Redirect(returnUrl ?? ExternalProviders.FrontendUrl(language, "account", "reauthenticated=1"));
    }

    private async Task<string?> FindSchemeAsync(string provider) =>
        (await signIn.GetExternalAuthenticationSchemesAsync())
            .FirstOrDefault(scheme => string.Equals(scheme.Name, provider, StringComparison.OrdinalIgnoreCase))?.Name;

    private static bool IsEmailVerified(ExternalLoginInfo info) =>
        string.Equals(info.Principal.FindFirstValue(ExternalProviders.EmailVerifiedClaim), "true", StringComparison.OrdinalIgnoreCase);

    private static string Method(ExternalLoginInfo info) => info.LoginProvider.ToLowerInvariant();

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Changing the external logins failed: {string.Join(", ", result.Errors.Select(error => error.Code))}");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} registered with {Provider}")]
    private static partial void LogRegistered(ILogger logger, Guid userId, string provider);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} signed in with {Provider}")]
    private static partial void LogSignedIn(ILogger logger, Guid userId, string provider);
}
