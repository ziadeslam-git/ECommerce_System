using ECommerce_System.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce_System.Data.EntityConfigurations;

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.HasKey(ci => ci.Id);

        builder.Property(ci => ci.Quantity)
            .IsRequired();

        // Quantity CHECK > 0
        builder.ToTable(t => t.HasCheckConstraint("CK_CartItems_Quantity", "[Quantity] > 0"));

        builder.Property(ci => ci.PriceSnapshot)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        // Composite unique: (CartId, ProductVariantId)
        builder.HasIndex(ci => new { ci.CartId, ci.ProductVariantId }).IsUnique();

        // CartItem → ProductVariant (no cascade — variant can exist independently)
        builder.HasOne(ci => ci.ProductVariant)
            .WithMany(v => v.CartItems)
            .HasForeignKey(ci => ci.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Matching query filter: hide CartItems whose variant is inactive (mirrors ProductVariant filter)
        builder.HasQueryFilter(ci => ci.ProductVariant!.IsActive);
    }
}
