using BajkoweCuda.Infrastructure.Audit;
using BajkoweCuda.Infrastructure.Emails;
using BajkoweCuda.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BajkoweCuda.Infrastructure.Persistence;

/// <summary>The application database: ASP.NET Core Identity tables plus, in later phases, chat and orders.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<EmailOutboxMessage> EmailOutbox => Set<EmailOutboxMessage>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
