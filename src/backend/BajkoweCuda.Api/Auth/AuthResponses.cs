using System.Text.Json.Serialization;

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Roles = null)
{
    public static MeResponse Anonymous { get; } = new(Authenticated: false);
}

/// <summary><c>POST /api/auth/login</c> when a second factor is needed (admins, Phase 3 part 3).</summary>
public sealed record TwoFactorRequiredResponse(bool RequiresTwoFactor = true);

/// <summary><c>GET /api/account</c>.</summary>
public sealed record AccountResponse(
    Guid Id,
    string Email,
    string DisplayName,
    bool EmailConfirmed,
    string PreferredLanguage,
    DateTimeOffset CreatedAt);
