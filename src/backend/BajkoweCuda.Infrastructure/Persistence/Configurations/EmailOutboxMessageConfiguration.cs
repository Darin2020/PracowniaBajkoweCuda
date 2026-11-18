using BajkoweCuda.Infrastructure.Emails;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BajkoweCuda.Infrastructure.Persistence.Configurations;

internal sealed class EmailOutboxMessageConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable("EmailOutbox");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();

        builder.Property(message => message.Kind).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
        builder.Property(message => message.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        builder.Property(message => message.ToEmail).HasMaxLength(EmailOutboxMessage.AddressMaxLength);
        builder.Property(message => message.Language).HasMaxLength(5).IsUnicode(false);
        builder.Property(message => message.Template).HasMaxLength(EmailOutboxMessage.TemplateMaxLength).IsUnicode(false);
        builder.Property(message => message.LastError).HasMaxLength(EmailOutboxMessage.LastErrorMaxLength);

        // The dispatcher's query: pending messages that are due, oldest first.
        builder.HasIndex(message => new { message.Status, message.SendAfter });

        // At most one pending chat notification per recipient and conversation: later messages join it (§3.6).
        builder.HasIndex(message => new { message.ToUserId, message.ConversationId })
            .IsUnique()
            .HasFilter("[Kind] = 'ChatDigest' AND [Status] = 'Pending'");
    }
}
