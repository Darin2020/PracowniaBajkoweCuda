namespace BajkoweCuda.Application.Audit;

/// <summary>
/// The audit log (docs/PLAN.md §5, AuditLog; §8.1): sign-ins, failed sign-ins, 2FA changes, account changes and admin
/// actions. Each entry is saved at once, independent of the caller's unit of work.
/// </summary>
public interface IAuditLog
{
    /// <param name="action">One of <see cref="AuditActions"/>.</param>
    /// <param name="actorId">The user who acted, or <c>null</c> when unknown (a failed sign-in with an unknown email).</param>
    /// <param name="entity">What the action was about, when it is not the actor's own account.</param>
    /// <param name="details">
    /// Small JSON-serializable object with extra facts. Never passwords, tokens, codes, message bodies or full email
    /// addresses (CLAUDE.md, Rules).
    /// </param>
    /// <param name="cancellationToken">Cancels saving the entry.</param>
    Task WriteAsync(
        string action,
        Guid? actorId,
        AuditEntity? entity = null,
        object? details = null,
        CancellationToken cancellationToken = default);
}

/// <summary>The subject of an audit entry, for example <c>("User", "&lt;id&gt;")</c>.</summary>
public sealed record AuditEntity(string Type, string Id);
