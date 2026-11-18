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

    /// <summary>The password does not meet the policy; <c>errors.password</c> lists Identity's codes (e.g. PasswordTooShort).</summary>
    public static AppError PasswordRejected(IEnumerable<IdentityError> errors) =>
        new("auth.password_rejected", ErrorKind.Validation, new Dictionary<string, string[]>
        {
            ["password"] = errors.Select(error => error.Code).Distinct().ToArray(),
        });
}
