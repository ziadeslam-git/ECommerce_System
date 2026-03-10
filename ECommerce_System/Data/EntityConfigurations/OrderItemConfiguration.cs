using ECommerce_System.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce_System.Data.EntityConfigurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.HasKey(oi => oi.Id);

        builder.Property(oi => oi.ProductName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(oi => oi.Size)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(oi => oi.Color)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(oi => oi.Quantity).IsRequired();
        builder.ToTable(t => t.HasCheckConstraint("CK_OrderItems_Quantity", "[Quantity] > 0"));

        builder.Property(oi => oi.UnitPrice)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(oi => oi.Subtotal)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        // OrderItem → ProductVariant (NO ACTION — keep history even if variant changes)
        builder.HasOne(oi => oi.ProductVariant)
            .WithMany(v => v.OrderItems)
            .HasForeignKey(oi => oi.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
