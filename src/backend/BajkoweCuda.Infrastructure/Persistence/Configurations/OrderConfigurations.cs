using BajkoweCuda.Domain.Chat;
using BajkoweCuda.Domain.Orders;
using BajkoweCuda.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BajkoweCuda.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    /// <summary>The database sequence behind order numbers (BC-0001, BC-0002, ...), docs/PLAN.md §3.5.</summary>
    public const string NumberSequence = "OrderNumbers";

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();

        builder.Property(order => order.Number)
            .HasDefaultValueSql($"NEXT VALUE FOR [{NumberSequence}]")
            .ValueGeneratedOnAdd();
        builder.HasIndex(order => order.Number).IsUnique();
        builder.Ignore(order => order.DisplayNumber);
        builder.Ignore(order => order.IsVisibleToCustomer);
        builder.Ignore(order => order.IsFinal);

        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(order => order.Title).HasMaxLength(Order.TitleMaxLength);
        builder.Property(order => order.Description).HasMaxLength(Order.DescriptionMaxLength);
        builder.Property(order => order.AdminNote).HasMaxLength(Order.AdminNoteMaxLength);
        builder.Property(order => order.CancellationReason).HasMaxLength(Order.ReasonMaxLength);
        builder.Property(order => order.RowVersion).IsRowVersion();

        builder.ComplexProperty(order => order.Price, price =>
        {
            price.Property(money => money.Amount).HasColumnName("PriceAmount").HasPrecision(10, 2);
            price.Property(money => money.Currency).HasColumnName("PriceCurrency").HasMaxLength(3).IsUnicode(false);
        });

        // The customer row stays (anonymized) when the account is deleted, so the order keeps pointing to it (§8.6).
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(order => order.CustomerId).OnDelete(DeleteBehavior.Restrict);
        // Deleting the account deletes its conversation and images; the order stays, without them.
        builder.HasOne<Conversation>().WithMany().HasForeignKey(order => order.ConversationId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Attachment>().WithMany().HasForeignKey(order => order.ReferenceImageId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(order => order.Photos).WithOne().HasForeignKey(photo => photo.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(order => order.Photos).HasField("_photos").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(order => order.StatusChanges).WithOne().HasForeignKey(change => change.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(order => order.StatusChanges).HasField("_statusChanges").UsePropertyAccessMode(PropertyAccessMode.Field);

        // "My orders" and the side panel of a conversation; the admin list filters by status and sorts by date.
        builder.HasIndex(order => new { order.CustomerId, order.CreatedAt });
        builder.HasIndex(order => order.ConversationId);
        builder.HasIndex(order => new { order.Status, order.CreatedAt });
        builder.HasIndex(order => order.ReferenceImageId);
    }
}

internal sealed class OrderPhotoConfiguration : IEntityTypeConfiguration<OrderPhoto>
{
    public void Configure(EntityTypeBuilder<OrderPhoto> builder)
    {
        builder.ToTable("OrderPhotos");
        builder.HasKey(photo => photo.Id);
        builder.Property(photo => photo.Id).ValueGeneratedNever();
        // The application deletes the attachment (and its blobs) when a photo is removed from an order.
        builder.HasOne<Attachment>().WithMany().HasForeignKey(photo => photo.AttachmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(photo => photo.AttachmentId).IsUnique();
    }
}

internal sealed class OrderStatusChangeConfiguration : IEntityTypeConfiguration<OrderStatusChange>
{
    public void Configure(EntityTypeBuilder<OrderStatusChange> builder)
    {
        builder.ToTable("OrderStatusChanges");
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).ValueGeneratedNever();
        builder.Property(change => change.FromStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(change => change.ToStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(change => change.Comment).HasMaxLength(Order.ReasonMaxLength);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(change => change.ChangedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(change => new { change.OrderId, change.ChangedAt });
    }
}
