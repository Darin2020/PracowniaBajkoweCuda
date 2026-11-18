namespace BajkoweCuda.Infrastructure.Accounts;

// "Export my data" (docs/PLAN.md §3.2, §3.7): everything stored about the user, as JSON. Later phases add the
// conversation and the orders.

public sealed record AccountExport(
    DateTimeOffset ExportedAt,
    ExportedProfile Profile,
    IReadOnlyList<string> Roles,
    IReadOnlyList<ExportedLogin> ExternalLogins,
    IReadOnlyList<ExportedEmail> Emails,
    IReadOnlyList<ExportedAuditEntry> SecurityEvents);

public sealed record ExportedProfile(
    Guid Id,
    string? Email,
    bool EmailConfirmed,
    string DisplayName,
    string PreferredLanguage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? TermsAcceptedAt,
    string? TermsVersion,
    bool TwoFactorEnabled);

public sealed record ExportedLogin(string Provider, string? DisplayName);

public sealed record ExportedEmail(string Template, string To, DateTimeOffset CreatedAt, string Status, DateTimeOffset? SentAt);

public sealed record ExportedAuditEntry(string Action, DateTimeOffset At, string? IpAddress);
