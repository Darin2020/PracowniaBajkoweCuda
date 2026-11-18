namespace BajkoweCuda.Infrastructure.Identity;

/// <summary>
/// <c>Admin:Emails</c>: the addresses whose accounts are admins (docs/PLAN.md §3.3, §14). An entry may also hold
/// several addresses separated by commas or semicolons, so a whole list fits in one environment variable
/// (<c>Admin__Emails__0</c>).
/// </summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public IList<string> Emails { get; } = [];

    public IReadOnlyList<string> Addresses() =>
    [
        .. Emails
            .SelectMany(entry => entry.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];
}
