using BajkoweCuda.Application.Files;
using BajkoweCuda.Domain.Chat;
using BajkoweCuda.Infrastructure.Identity;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BajkoweCuda.Infrastructure.Accounts;

/// <summary>Data export and erasure of one account (docs/PLAN.md §3.7, §8.6).</summary>
public sealed partial class AccountData(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    IFileStore files,
    TimeProvider time,
    ILogger<AccountData> logger)
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
            events,
            await ExportConversationAsync(id, cancellationToken));
    }

    private async Task<ExportedConversation?> ExportConversationAsync(Guid userId, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(row => row.CustomerId == userId, cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        var messages = await db.Messages.AsNoTracking()
            .Where(message => message.ConversationId == conversation.Id)
            .OrderBy(message => message.CreatedAt).ThenBy(message => message.Id)
            .ToListAsync(cancellationToken);
        var images = (await db.Attachments.AsNoTracking()
                .Where(attachment => db.Messages.Any(message => message.Id == attachment.MessageId && message.ConversationId == conversation.Id))
                .Select(attachment => new { attachment.Id, attachment.MessageId })
                .ToListAsync(cancellationToken))
            .ToLookup(attachment => attachment.MessageId!.Value, attachment => $"/api/files/{attachment.Id}");

        return new ExportedConversation(
            conversation.CreatedAt,
            conversation.CustomerEmailNotificationsEnabled,
            [.. messages.Select(message => new ExportedMessage(
                message.CreatedAt,
                message.From == ChatSide.Customer ? "you" : "studio",
                message.Kind.ToString(),
                message.Body,
                message.SystemEventType,
                [.. images[message.Id]]))]);
    }

    /// <summary>
    /// Erases the personal data of the account (§8.6): name, email, password, external logins, 2FA, roles, queued and
    /// sent emails, the IP addresses in its audit entries, and the conversation with all its messages and images
    /// (the blobs too, once the rows are gone). The row stays, anonymized and marked deleted, because
    /// other records (orders, from Phase 5) keep pointing to it. Its new security stamp ends every session.
    /// </summary>
    public async Task EraseAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var id = user.Id;
        var blobs = new List<(string Container, string Name)>();
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            blobs.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // The conversation's images (whoever sent them) and the user's own uploads that were never sent.
            var conversationIds = db.Conversations.Where(row => row.CustomerId == id).Select(row => row.Id);
            var attachments = db.Attachments.Where(row =>
                row.OwnerUserId == id && row.MessageId == null
                || db.Messages.Any(message => message.Id == row.MessageId && conversationIds.Contains(message.ConversationId)));
            foreach (var row in await attachments.Select(row => new { row.Container, row.BlobName, row.ThumbnailBlobName }).ToListAsync(cancellationToken))
            {
                blobs.Add((row.Container, row.BlobName));
                blobs.Add((row.Container, row.ThumbnailBlobName));
            }

            await attachments.ExecuteDeleteAsync(cancellationToken);
            await db.Messages.Where(row => conversationIds.Contains(row.ConversationId)).ExecuteDeleteAsync(cancellationToken);
            await db.Conversations.Where(row => row.CustomerId == id).ExecuteDeleteAsync(cancellationToken);

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

        await DeleteBlobsAsync(blobs);
    }

    /// <summary>
    /// After the commit, so a failure cannot bring rows back. A blob that cannot be deleted now is logged (by name
    /// only) for the operator; nothing points to it any more.
    /// </summary>
    private async Task DeleteBlobsAsync(List<(string Container, string Name)> blobs)
    {
        foreach (var (container, name) in blobs)
        {
            try
            {
                await files.DeleteAsync(container, name, CancellationToken.None);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                LogBlobNotDeleted(logger, error, container, name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Blob {Container}/{BlobName} of a deleted account could not be deleted")]
    private static partial void LogBlobNotDeleted(ILogger logger, Exception error, string container, string blobName);
}
