using System.Text.Json.Serialization;
using BajkoweCuda.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace BajkoweCuda.Api.Auth;

/// <summary>
/// <c>GET /api/auth/me</c>: <c>{ "authenticated": false }</c> for visitors, the user summary otherwise (docs/PLAN.md §6).
/// </summary>
public sealed record MeResponse(
    bool Authenticated,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? Id = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Email = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DisplayName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? EmailConfirmed = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PreferredLanguage = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Roles = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? TwoFactorEnabled = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? UnreadCount = null)
{
    public static MeResponse Anonymous { get; } = new(Authenticated: false);

    /// <summary>
    /// The summary of a signed-in user. An admin with <c>twoFactorEnabled: false</c> must set up 2FA before the admin
    /// endpoints answer (they return <c>auth.mfa_required</c>). <paramref name="unreadCount"/> is the chat badge (§3.4):
    /// the customer's unread studio messages, or for an admin with 2FA all unread customer messages.
    /// </summary>
    public static async Task<MeResponse> ForAsync(UserManager<ApplicationUser> users, ApplicationUser user, int? unreadCount = null) => new(
        Authenticated: true,
        Id: user.Id,
        Email: user.Email,
        DisplayName: user.DisplayName,
        EmailConfirmed: user.EmailConfirmed,
        PreferredLanguage: user.PreferredLanguage,
        Roles: [.. await users.GetRolesAsync(user)],
        TwoFactorEnabled: user.TwoFactorEnabled,
        UnreadCount: unreadCount);
}

/// <summary><c>POST /api/auth/login</c> when the account has 2FA: continue with <c>POST /api/auth/2fa/login</c>.</summary>
public sealed record TwoFactorRequiredResponse(bool RequiresTwoFactor = true);

/// <summary><c>GET /api/account</c>.</summary>
public sealed record AccountResponse(
    Guid Id,
    string Email,
    string DisplayName,
    bool EmailConfirmed,
    string PreferredLanguage,
    DateTimeOffset CreatedAt);

/// <summary><c>GET /api/auth/2fa/setup</c>: the key to enter in an authenticator app, also as an otpauth:// URI for a QR code.</summary>
public sealed record TwoFactorSetupResponse(string SharedKey, string AuthenticatorUri);

/// <summary>One-time recovery codes, shown once (<c>POST 2fa/enable</c>, <c>POST 2fa/recovery-codes</c>).</summary>
public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

/// <summary><c>GET /api/admin/audit</c>: newest first; pass <c>nextCursor</c> as <c>cursor</c> for older entries.</summary>
public sealed record AuditPageResponse(IReadOnlyList<AuditEntryResponse> Items, long? NextCursor);

public sealed record AuditEntryResponse(
    long Id,
    DateTimeOffset CreatedAt,
    string Action,
    Guid? ActorId,
    string? EntityType,
    string? EntityId,
    string? IpAddress,
    System.Text.Json.JsonElement? Details);

/// <summary><c>GET /api/auth/complete-profile</c>: what the provider told about the person signing up.</summary>
public sealed record PendingProfileResponse(string Provider, string? Email, bool EmailVerified, string? DisplayName);

/// <summary><c>GET /api/account/logins</c>: how the user can sign in, and which providers can still be linked.</summary>
public sealed record AccountLoginsResponse(bool HasPassword, IReadOnlyList<LinkedLoginResponse> Logins, IReadOnlyList<string> Available);

public sealed record LinkedLoginResponse(string Provider, string? DisplayName);
