using BajkoweCuda.Infrastructure.Identity;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BajkoweCuda.Infrastructure.Accounts;

/// <summary>Data export and erasure of one account (docs/PLAN.md §3.7, §8.6).</summary>
public sealed class AccountData(AppDbContext db, UserManager<ApplicationUser> users, TimeProvider time)
{
    public async Task<AccountExport> ExportAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var id = user.Id;
        var logins = await db.UserLogins
            .Where(login => login.UserId == id)
            .OrderBy(login => login.LoginProvider)
            .Select(login => new ExportedLogin(login.LoginProvider, login.ProviderDisplayName))
            .ToListAsync(cancellationToken);

        var emails = await db.EmailOutbox
            .Where(message => message.ToUserId == id)
            .OrderBy(message => message.CreatedAt)
            .Select(message => new { message.Template, message.ToEmail, message.CreatedAt, message.Status, message.SentAt })
            .ToListAsync(cancellationToken);

        var events = await db.AuditLog
            .Where(entry => entry.ActorId == id)
            .OrderBy(entry => entry.Id)
            .Select(entry => new ExportedAuditEntry(entry.Action, entry.CreatedAt, entry.IpAddress))
            .ToListAsync(cancellationToken);

        return new AccountExport(
            time.GetUtcNow(),
            new ExportedProfile(
                user.Id,
                user.Email,
                user.EmailConfirmed,
                user.DisplayName,
                user.PreferredLanguage,
                user.CreatedAt,
                user.TermsAcceptedAt,
                user.TermsVersion,
                user.TwoFactorEnabled),
            [.. await users.GetRolesAsync(user)],
            logins,
            [.. emails.Select(message => new ExportedEmail(message.Template, message.ToEmail, message.CreatedAt, message.Status.ToString(), message.SentAt))],
            events);
    }

    /// <summary>
    /// Erases the personal data of the account (§8.6): name, email, password, external logins, 2FA, roles, queued and
    /// sent emails, and the IP addresses in its audit entries. The row stays, anonymized and marked deleted, because
    /// other records (orders, from Phase 5) keep pointing to it. Its new security stamp ends every session.
    /// </summary>
    public async Task EraseAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var id = user.Id;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            await db.UserLogins.Where(row => row.UserId == id).ExecuteDeleteAsync(cancellationToken);
            await db.UserTokens.Where(row => row.UserId == id).ExecuteDeleteAsync(cancellationToken);
            await db.UserClaims.Where(row => row.UserId == id).ExecuteDeleteAsync(cancellationToken);
            await db.UserRoles.Where(row => row.UserId == id).ExecuteDeleteAsync(cancellationToken);
            await db.EmailOutbox.Where(row => row.ToUserId == id).ExecuteDeleteAsync(cancellationToken);
            await db.AuditLog
                .Where(row => row.ActorId == id && row.IpAddress != null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IpAddress, (string?)null), cancellationToken);

            var deletedName = $"deleted-{id:N}";
            user.UserName = deletedName;
            user.NormalizedUserName = users.NormalizeName(deletedName);
            user.Email = null;
            user.NormalizedEmail = null;
            user.EmailConfirmed = false;
            user.PasswordHash = null;
            user.PhoneNumber = null;
            user.PhoneNumberConfirmed = false;
            user.TwoFactorEnabled = false;
            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.MaxValue;
            user.AccessFailedCount = 0;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.DisplayName = string.Empty;
            user.TermsAcceptedAt = null;
            user.TermsVersion = null;
            user.IsDeleted = true;
            user.DeletedAt = time.GetUtcNow();

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }
}
