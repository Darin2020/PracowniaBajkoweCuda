using System.Text.Json;
using BajkoweCuda.Application.Audit;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BajkoweCuda.Infrastructure.Audit;

/// <summary>
/// Writes audit entries through a context of its own, so an entry is saved even when the caller's changes are not,
/// and saving it never saves the caller's pending changes by accident.
/// </summary>
internal sealed class AuditLog(IServiceScopeFactory scopes, IClientInfo client, TimeProvider time) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task WriteAsync(
        string action,
        Guid? actorId,
        AuditEntity? entity = null,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        var detailsJson = details is null ? null : JsonSerializer.Serialize(details, Json);
        if (detailsJson is { Length: > AuditLogEntry.DetailsMaxLength })
        {
            throw new ArgumentException("Audit details are limited to a few small values.", nameof(details));
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AuditLog.Add(new AuditLogEntry
        {
            ActorId = actorId,
            Action = action,
            EntityType = entity?.Type,
            EntityId = entity?.Id,
            CreatedAt = time.GetUtcNow(),
            IpAddress = client.IpAddress,
            DetailsJson = detailsJson,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Outside a request there is no client.</summary>
internal sealed class NoClientInfo : IClientInfo
{
    public string? IpAddress => null;
}
