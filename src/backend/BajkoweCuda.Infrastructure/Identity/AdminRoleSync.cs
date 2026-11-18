using BajkoweCuda.Application.Audit;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BajkoweCuda.Infrastructure.Identity;

/// <summary>
/// Keeps the Admin role in step with <c>Admin:Emails</c> (docs/PLAN.md §3.3): an account is an admin exactly when its
/// email is listed and confirmed. Admins are never created through registration. The application checks a user when
/// they sign in or confirm their email; the migration job checks everybody on every deployment
/// (docs/DECISIONS.md), so the serverless database is not woken by an application start.
/// </summary>
public sealed partial class AdminRoleSync(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    IOptions<AdminOptions> options,
    IAuditLog audit,
    ILogger<AdminRoleSync> logger)
{
    /// <summary>Grants or revokes the role of one user. Returns whether the user is an admin now.</summary>
    public async Task<bool> SyncUserAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var listed = ListedEmails();
        var shouldBeAdmin = ShouldBeAdmin(user, listed);
        var isAdmin = await users.IsInRoleAsync(user, Roles.Admin);

        if (shouldBeAdmin && !isAdmin)
        {
            await GrantAsync(user, cancellationToken);
        }
        else if (!shouldBeAdmin && isAdmin)
        {
            await RevokeAsync(user, cancellationToken);
        }

        return shouldBeAdmin;
    }

    /// <summary>Checks every listed account and every current admin.</summary>
    public async Task SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var listed = ListedEmails();
        if (listed.Count == 0)
        {
            LogNoAdmins(logger);
        }

        foreach (var admin in await users.GetUsersInRoleAsync(Roles.Admin))
        {
            if (!ShouldBeAdmin(admin, listed))
            {
                await RevokeAsync(admin, cancellationToken);
            }
        }

        foreach (var email in listed)
        {
            if (await users.FindByEmailAsync(email) is { } user
                && ShouldBeAdmin(user, listed)
                && !await users.IsInRoleAsync(user, Roles.Admin))
            {
                await GrantAsync(user, cancellationToken);
            }
        }
    }

    private HashSet<string> ListedEmails() =>
        options.Value.Addresses().Select(users.NormalizeEmail).OfType<string>().ToHashSet(StringComparer.Ordinal);

    private static bool ShouldBeAdmin(ApplicationUser user, HashSet<string> listed) =>
        !user.IsDeleted && user.EmailConfirmed && user.NormalizedEmail is { } email && listed.Contains(email);

    private async Task GrantAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (!await roles.RoleExistsAsync(Roles.Admin))
        {
            Check(await roles.CreateAsync(new IdentityRole<Guid>(Roles.Admin)));
        }

        Check(await users.AddToRoleAsync(user, Roles.Admin));
        await audit.WriteAsync(AuditActions.AdminRoleGranted, actorId: null, new AuditEntity("User", user.Id.ToString()), cancellationToken: cancellationToken);
        LogGranted(logger, user.Id);
    }

    private async Task RevokeAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        Check(await users.RemoveFromRoleAsync(user, Roles.Admin));
        // A new security stamp makes the open sessions re-read their roles at the next validation.
        Check(await users.UpdateSecurityStampAsync(user));
        await audit.WriteAsync(AuditActions.AdminRoleRevoked, actorId: null, new AuditEntity("User", user.Id.ToString()), cancellationToken: cancellationToken);
        LogRevoked(logger, user.Id);
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Changing the Admin role failed: {string.Join(", ", result.Errors.Select(error => error.Code))}");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} is now an admin")]
    private static partial void LogGranted(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} is no longer an admin")]
    private static partial void LogRevoked(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Admin:Emails is empty: nobody is an admin")]
    private static partial void LogNoAdmins(ILogger logger);
}
