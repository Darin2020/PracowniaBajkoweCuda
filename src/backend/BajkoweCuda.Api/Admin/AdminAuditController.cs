using System.Text.Json;
using BajkoweCuda.Api.Auth;
using BajkoweCuda.Api.Security;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BajkoweCuda.Api.Admin;

/// <summary>The audit log for admins (docs/PLAN.md §6, <c>GET /api/admin/audit</c>).</summary>
[ApiController]
[Route("api/admin/audit")]
[Authorize(Policy = AuthSetup.AdminPolicy)]
[EnableRateLimiting(RateLimiting.Admin)]
public sealed class AdminAuditController(AppDbContext db) : ControllerBase
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    /// <summary>Newest first. <paramref name="cursor"/> is the <c>nextCursor</c> of the previous page.</summary>
    [HttpGet]
    public async Task<AuditPageResponse> Get(long? cursor, int take = DefaultPageSize, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, MaxPageSize);
        var query = db.AuditLog.AsNoTracking();
        if (cursor is { } before)
        {
            query = query.Where(entry => entry.Id < before);
        }

        var rows = await query.OrderByDescending(entry => entry.Id).Take(take + 1).ToListAsync(cancellationToken);
        var items = rows.Take(take).Select(entry => new AuditEntryResponse(
            entry.Id,
            entry.CreatedAt,
            entry.Action,
            entry.ActorId,
            entry.EntityType,
            entry.EntityId,
            entry.IpAddress,
            entry.DetailsJson is null ? null : JsonSerializer.Deserialize<JsonElement>(entry.DetailsJson))).ToList();

        return new AuditPageResponse(items, rows.Count > take ? items[^1].Id : null);
    }
}
