using BajkoweCuda.Infrastructure.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BajkoweCuda.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLog");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).UseIdentityColumn();

        builder.Property(entry => entry.Action).HasMaxLength(AuditLogEntry.ActionMaxLength).IsUnicode(false);
        builder.Property(entry => entry.EntityType).HasMaxLength(AuditLogEntry.EntityTypeMaxLength).IsUnicode(false);
        builder.Property(entry => entry.EntityId).HasMaxLength(AuditLogEntry.EntityIdMaxLength).IsUnicode(false);
        builder.Property(entry => entry.IpAddress).HasMaxLength(AuditLogEntry.IpAddressMaxLength).IsUnicode(false);
        builder.Property(entry => entry.DetailsJson).HasMaxLength(AuditLogEntry.DetailsMaxLength);

        // A user's own entries: data export and account deletion.
        builder.HasIndex(entry => entry.ActorId);
    }
}
