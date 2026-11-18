using BajkoweCuda.Domain.Chat;
using BajkoweCuda.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BajkoweCuda.Infrastructure.Persistence.Configurations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");
        builder.HasKey(conversation => conversation.Id);
        builder.Property(conversation => conversation.Id).ValueGeneratedNever();

        // One conversation per customer (docs/PLAN.md §3.4). Deleting an account deletes its conversation first (§8.6).
        builder.HasIndex(conversation => conversation.CustomerId).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(conversation => conversation.CustomerId).OnDelete(DeleteBehavior.Restrict);

        // The admin's list: newest conversations first.
        builder.HasIndex(conversation => conversation.LastMessageAt);
    }
}

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();

        builder.Property(message => message.From).HasConversion<byte>();
        builder.Property(message => message.Kind).HasConversion<byte>();
        builder.Property(message => message.Body).HasMaxLength(Message.BodyMaxLength);
        builder.Property(message => message.SystemEventType).HasMaxLength(Message.SystemEventTypeMaxLength).IsUnicode(false);
        builder.Property(message => message.SystemEventPayload).HasMaxLength(Message.SystemEventPayloadMaxLength);

        builder.HasOne<Conversation>().WithMany().HasForeignKey(message => message.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(message => message.SenderId).OnDelete(DeleteBehavior.Restrict);

        // History pages (newest first, cursor = the last message's time and id).
        builder.HasIndex(message => new { message.ConversationId, message.CreatedAt, message.Id });
    }
}

internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments");
        builder.HasKey(attachment => attachment.Id);
        builder.Property(attachment => attachment.Id).ValueGeneratedNever();

        builder.Property(attachment => attachment.Container).HasMaxLength(Attachment.ContainerMaxLength).IsUnicode(false);
        builder.Property(attachment => attachment.BlobName).HasMaxLength(Attachment.BlobNameMaxLength).IsUnicode(false);
        builder.Property(attachment => attachment.ThumbnailBlobName).HasMaxLength(Attachment.BlobNameMaxLength).IsUnicode(false);
        builder.Property(attachment => attachment.ContentType).HasMaxLength(Attachment.ContentTypeMaxLength).IsUnicode(false);

        // No cascade from messages: deleting the rows must also delete the blobs, which the application does.
        builder.HasOne<Message>().WithMany().HasForeignKey(attachment => attachment.MessageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(attachment => attachment.OwnerUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(attachment => attachment.MessageId);
        builder.HasIndex(attachment => attachment.OwnerUserId);
        // The cleanup looks for uploads that were never sent.
        builder.HasIndex(attachment => attachment.CreatedAt).HasFilter("[MessageId] IS NULL");
    }
}
