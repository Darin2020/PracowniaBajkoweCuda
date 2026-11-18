using System.ComponentModel.DataAnnotations;
using BajkoweCuda.Domain;
using BajkoweCuda.Infrastructure.Identity;

namespace BajkoweCuda.Api.Auth;

// Request shapes of /api/auth and /api/account. Data annotations check the shape; the password policy is
// Identity's (AuthSetup), so a weak password is reported with Identity's codes (auth.password_rejected).

public sealed class RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(128)]
    public string Password { get; init; } = string.Empty;

    [Required, MaxLength(ApplicationUser.DisplayNameMaxLength)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, SupportedLanguage]
    public string PreferredLanguage { get; init; } = Languages.Default;

    /// <summary>The Terms and the Privacy policy must be accepted (§3.2).</summary>
    [Range(typeof(bool), "true", "true", ErrorMessage = "The terms must be accepted.")]
    public bool AcceptTerms { get; init; }
}

public sealed class LoginRequest
{
    [Required, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(128)]
    public string Password { get; init; } = string.Empty;
}

public sealed class EmailRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;
}

public sealed class ConfirmEmailRequest
{
    public Guid UserId { get; init; }

    [Required, MaxLength(2048)]
    public string Code { get; init; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    public Guid UserId { get; init; }

    [Required, MaxLength(2048)]
    public string Code { get; init; } = string.Empty;

    [Required, MaxLength(128)]
    public string Password { get; init; } = string.Empty;
}

public sealed class UpdateAccountRequest
{
    [MinLength(1), MaxLength(ApplicationUser.DisplayNameMaxLength)]
    public string? DisplayName { get; init; }

    [SupportedLanguage]
    public string? PreferredLanguage { get; init; }
}

public sealed class TwoFactorCodeRequest
{
    /// <summary>The 6-digit code of the authenticator app (spaces and dashes are ignored).</summary>
    [Required, MaxLength(32)]
    public string Code { get; init; } = string.Empty;
}

public sealed class TwoFactorLoginRequest
{
    /// <summary>The authenticator code, or a recovery code when <see cref="IsRecoveryCode"/> is set.</summary>
    [Required, MaxLength(32)]
    public string Code { get; init; } = string.Empty;

    public bool IsRecoveryCode { get; init; }
}

/// <summary>Re-authentication before deleting the account (§8.6).</summary>
public sealed class DeleteAccountRequest
{
    [Required, MaxLength(128)]
    public string Password { get; init; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [Required, MaxLength(128)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required, MaxLength(128)]
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>One of the supported languages (en, de, fr, es, pl); <c>null</c> is left to [Required].</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SupportedLanguageAttribute : ValidationAttribute
{
    public SupportedLanguageAttribute()
        : base("Unsupported language.")
    {
    }

    public override bool IsValid(object? value) => value is null || (value is string text && Languages.Find(text) is not null);
}
