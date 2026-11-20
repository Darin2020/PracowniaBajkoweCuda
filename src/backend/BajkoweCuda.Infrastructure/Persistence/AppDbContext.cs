using BajkoweCuda.Domain.Chat;
using BajkoweCuda.Domain.Orders;
using BajkoweCuda.Infrastructure.Audit;
using BajkoweCuda.Infrastructure.Emails;
using BajkoweCuda.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BajkoweCuda.Infrastructure.Persistence;

/// <summary>The application database: ASP.NET Core Identity tables the chat and the orders.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<EmailOutboxMessage> EmailOutbox => Set<EmailOutboxMessage>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderPhoto> OrderPhotos => Set<OrderPhoto>();

    public DbSet<OrderStatusChange> OrderStatusChanges => Set<OrderStatusChange>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasSequence<int>(Configurations.OrderConfiguration.NumberSequence).StartsAt(1).IncrementsBy(1);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
