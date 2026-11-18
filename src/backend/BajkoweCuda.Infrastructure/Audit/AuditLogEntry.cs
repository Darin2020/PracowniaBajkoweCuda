namespace BajkoweCuda.Infrastructure.Audit;

/// <summary>A row of the audit log (docs/PLAN.md §5, AuditLog).</summary>
public sealed class AuditLogEntry
{
    public const int ActionMaxLength = 64;
    public const int EntityTypeMaxLength = 64;
    public const int EntityIdMaxLength = 64;
    public const int IpAddressMaxLength = 45;
    public const int DetailsMaxLength = 2000;

    /// <summary>Increasing number: the order of the log and the paging cursor.</summary>
    public long Id { get; set; }

    public Guid? ActorId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? IpAddress { get; set; }

    public string? DetailsJson { get; set; }
}
