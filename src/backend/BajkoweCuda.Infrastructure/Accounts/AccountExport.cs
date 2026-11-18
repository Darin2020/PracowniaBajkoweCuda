namespace BajkoweCuda.Infrastructure.Accounts;

// "Export my data" (docs/PLAN.md §3.2, §3.7): everything stored about the user, as JSON. Later phases add the
// orders.

public sealed record AccountExport(
    DateTimeOffset ExportedAt,
    ExportedProfile Profile,
    IReadOnlyList<string> Roles,
    IReadOnlyList<ExportedLogin> ExternalLogins,
    IReadOnlyList<ExportedEmail> Emails,
    IReadOnlyList<ExportedAuditEntry> SecurityEvents,
    ExportedConversation? Conversation);

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

/// <summary>The chat with the studio. Images are listed with their address in the app; the files stay downloadable there.</summary>
public sealed record ExportedConversation(
    DateTimeOffset CreatedAt,
    bool EmailNotificationsEnabled,
    IReadOnlyList<ExportedMessage> Messages);

public sealed record ExportedMessage(
    DateTimeOffset CreatedAt,
    string From,
    string Kind,
    string Body,
    string? SystemEventType,
    IReadOnlyList<string> Images);
