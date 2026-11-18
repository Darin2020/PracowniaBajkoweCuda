using System.Text;
using BajkoweCuda.Api.Problems;
using BajkoweCuda.Api.Security;
using BajkoweCuda.Application.Audit;
using BajkoweCuda.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BajkoweCuda.Api.Auth;

/// <summary>
/// TOTP two-factor authentication (docs/PLAN.md §3.3, §6, §8.1): mandatory for admins, available to everybody.
/// A session that passed the second factor carries <c>amr=mfa</c>, which the admin endpoints require.
/// </summary>
[ApiController]
[Route("api/auth/2fa")]
public sealed partial class TwoFactorController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IAuditLog audit,
    ILogger<TwoFactorController> logger) : ControllerBase
{
    public const string Issuer = "Pracownia Bajkowe Cuda";
    private const int RecoveryCodeCount = 10;

    /// <summary>The key for the authenticator app. Created on first use; enabling 2FA proves the app has it.</summary>
    [HttpGet("setup")]
    public async Task<IActionResult> Setup()
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeleted)
        {
            return AuthErrors.NotAuthenticated.ToResult();
        }

        if (user.TwoFactorEnabled)
        {
            return AuthErrors.TwoFactorAlreadyEnabled.ToResult();
        }

        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await users.ResetAuthenticatorKeyAsync(user);
            // Resetting the key changes the security stamp: keep this session valid.
            await signIn.RefreshSignInAsync(user);
            key = await users.GetAuthenticatorKeyAsync(user)
                ?? throw new InvalidOperationException("The authenticator key was not stored.");
        }

        return Ok(new TwoFactorSetupResponse(FormatKey(key), AuthenticatorUri(user.Email!, key)));
    }

    /// <summary>Turns 2FA on with a first valid code, returns the recovery codes and upgrades this session to <c>amr=mfa</c>.</summary>
    [HttpPost("enable")]
    [EnableRateLimiting(RateLimiting.Auth)]
    public async Task<IActionResult> Enable(TwoFactorCodeRequest request, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeleted)
        {
            return AuthErrors.NotAuthenticated.ToResult();
        }

        if (user.TwoFactorEnabled)
        {
            return AuthErrors.TwoFactorAlreadyEnabled.ToResult();
        }

        var valid = await users.GetAuthenticatorKeyAsync(user) is not null
            && await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, AuthenticatorCode(request.Code));
        if (!valid)
        {
            return AuthErrors.InvalidTwoFactorCode.ToResult();
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? [];

        // The code just proved the second factor. Re-issue the session with amr=mfa, keeping its sign-in time.
        var current = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        await signIn.SignInWithClaimsAsync(user, current.Properties, [new(AuthSetup.AuthenticationMethodClaim, AuthSetup.MultiFactorMethod)]);

        await audit.WriteAsync(AuditActions.TwoFactorEnabled, user.Id, cancellationToken: cancellationToken);
        LogEnabled(logger, user.Id);
        return Ok(new RecoveryCodesResponse([.. codes]));
    }

    /// <summary>The second step of signing in, after <c>POST /api/auth/login</c> answered <c>requiresTwoFactor</c>.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.Auth)]
    public async Task<IActionResult> Login(TwoFactorLoginRequest request, CancellationToken cancellationToken)
    {
        // Who passed the password step is kept in the short-lived two-factor cookie.
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null || user.IsDeleted)
        {
            return AuthErrors.InvalidCredentials.ToResult();
        }

        var result = request.IsRecoveryCode
            ? await signIn.TwoFactorRecoveryCodeSignInAsync(request.Code.Trim())
            : await signIn.TwoFactorAuthenticatorSignInAsync(AuthenticatorCode(request.Code), isPersistent: true, rememberClient: false);

        if (!result.Succeeded)
        {
            await audit.WriteAsync(AuditActions.TwoFactorLoginFailed, user.Id, details: new { lockedOut = result.IsLockedOut }, cancellationToken: cancellationToken);
            return AuthErrors.InvalidTwoFactorCode.ToResult();
        }

        var method = request.IsRecoveryCode ? "recovery_code" : "authenticator";
        await audit.WriteAsync(AuditActions.LoginSucceeded, user.Id, details: new { method }, cancellationToken: cancellationToken);
        return Ok(await this.CompleteSignInAsync(signIn, users, user, logger));
    }

    /// <summary>Replaces the recovery codes. Only in a session that passed the second factor.</summary>
    [HttpPost("recovery-codes")]
    [EnableRateLimiting(RateLimiting.Auth)]
    public async Task<IActionResult> RecoveryCodes(CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeleted)
        {
            return AuthErrors.NotAuthenticated.ToResult();
        }

        if (!user.TwoFactorEnabled)
        {
            return AuthErrors.TwoFactorNotEnabled.ToResult();
        }

        if (!User.HasMultiFactor())
        {
            return AuthErrors.MfaRequired.ToResult();
        }

        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? [];
        await audit.WriteAsync(AuditActions.RecoveryCodesGenerated, user.Id, cancellationToken: cancellationToken);
        return Ok(new RecoveryCodesResponse([.. codes]));
    }

    /// <summary>Authenticator apps show "123 456"; people also type dashes.</summary>
    private static string AuthenticatorCode(string code) => code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);

    /// <summary>"abcd efgh ijkl ..." is easier to type than one long string.</summary>
    internal static string FormatKey(string key)
    {
        var result = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (i > 0)
            {
                result.Append(' ');
            }

            result.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }

        return result.ToString().ToLowerInvariant();
    }

    /// <summary>The otpauth:// URI that authenticator apps read from a QR code (Key URI format).</summary>
    internal static string AuthenticatorUri(string email, string key) =>
        $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(email)}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} enabled two-factor authentication")]
    private static partial void LogEnabled(ILogger logger, Guid userId);
}
