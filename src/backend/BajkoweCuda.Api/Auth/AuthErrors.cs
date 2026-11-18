using BajkoweCuda.Application.Common;
using Microsoft.AspNetCore.Identity;

namespace BajkoweCuda.Api.Auth;

/// <summary>Error codes of the auth and account endpoints. Keep in step with the frontend's translations.</summary>
internal static class AuthErrors
{
    public static readonly AppError NotAuthenticated = new("auth.not_authenticated", ErrorKind.Unauthorized);

    public static readonly AppError Forbidden = new("auth.forbidden", ErrorKind.Forbidden);

    /// <summary>Wrong email or password, locked out, or no such account: deliberately the same answer (§8.1).</summary>
    public static readonly AppError InvalidCredentials = new("auth.invalid_credentials", ErrorKind.Unauthorized);

    /// <summary>A confirmation or reset link that is wrong, used or expired.</summary>
    public static readonly AppError InvalidToken = new("auth.invalid_token", ErrorKind.Validation);

    /// <summary>An admin endpoint was called without a second factor in this session (§8.1): set up or use 2FA.</summary>
    public static readonly AppError MfaRequired = new("auth.mfa_required", ErrorKind.Forbidden);

    /// <summary>A wrong, used or expired authenticator or recovery code.</summary>
    public static readonly AppError InvalidTwoFactorCode = new("auth.invalid_2fa_code", ErrorKind.Validation);

    public static readonly AppError TwoFactorAlreadyEnabled = new("auth.2fa_already_enabled", ErrorKind.Conflict);

    public static readonly AppError TwoFactorNotEnabled = new("auth.2fa_not_enabled", ErrorKind.Conflict);

    /// <summary>
    /// The current password, asked again before a sensitive change (delete the account, change the password), was
    /// wrong, or the account has no password. Not 401: the session itself is still valid.
    /// </summary>
    public static readonly AppError ReauthenticationFailed = new("auth.reauthentication_failed", ErrorKind.Forbidden);

    /// <summary>Too many requests (§8.3); the response carries <c>Retry-After</c>.</summary>
    public static readonly AppError RateLimited = new("rate_limited", ErrorKind.TooManyRequests);

    /// <summary>The password does not meet the policy; <c>errors.password</c> lists Identity's codes (e.g. PasswordTooShort).</summary>
    public static AppError PasswordRejected(IEnumerable<IdentityError> errors) =>
        new("auth.password_rejected", ErrorKind.Validation, new Dictionary<string, string[]>
        {
            ["password"] = errors.Select(error => error.Code).Distinct().ToArray(),
        });
}
