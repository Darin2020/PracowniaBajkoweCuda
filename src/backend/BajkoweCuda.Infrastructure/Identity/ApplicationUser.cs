using Microsoft.AspNetCore.Identity;

namespace BajkoweCuda.Infrastructure.Identity;

/// <summary>
/// A person with an account: customer or admin (docs/PLAN.md §3.2, §5). It lives in Infrastructure, not in Domain,
/// because it derives from ASP.NET Core Identity and the Domain layer has no framework dependencies.
/// Domain entities refer to users by id only.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 100;
    public const int PreferredLanguageMaxLength = 5;
    public const int TermsVersionMaxLength = 32;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>One of en, de, fr, es, pl. Used for emails and system messages.</summary>
    public string PreferredLanguage { get; set; } = "en";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the user accepted the Terms and the Privacy policy, and which version (§3.2).</summary>
    public DateTimeOffset? TermsAcceptedAt { get; set; }

    public string? TermsVersion { get; set; }

    /// <summary>Set when the account is deleted and its personal data erased (§8.6).</summary>
    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}
