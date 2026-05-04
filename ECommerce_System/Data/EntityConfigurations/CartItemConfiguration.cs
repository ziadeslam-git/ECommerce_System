using ECommerce_System.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce_System.Data.EntityConfigurations;

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.HasKey(ci => ci.Id);

        builder.Property(ci => ci.ProductVariantId)
            .IsRequired(false);

        builder.Property(ci => ci.Quantity)
            .IsRequired();

        // Quantity CHECK > 0
        builder.ToTable(t => t.HasCheckConstraint("CK_CartItems_Quantity", "[Quantity] > 0"));

        builder.Property(ci => ci.PriceSnapshot)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(ci => ci.GiftBundleTitle)
            .HasMaxLength(200);

        builder.Property(ci => ci.GiftBundleOriginalTotal)
            .HasColumnType("decimal(18,2)");

        builder.Property(ci => ci.GiftBundleItemsJson)
            .HasColumnType("nvarchar(max)");

        // Composite unique: (CartId, ProductVariantId) for regular items only
        builder.HasIndex(ci => new { ci.CartId, ci.ProductVariantId })
            .IsUnique()
            .HasFilter("[ProductVariantId] IS NOT NULL");

        // Composite unique: (CartId, GiftBundleId) for bundle items only
        builder.HasIndex(ci => new { ci.CartId, ci.GiftBundleId })
            .IsUnique()
            .HasFilter("[GiftBundleId] IS NOT NULL");

        // CartItem → ProductVariant (no cascade — variant can exist independently)
        builder.HasOne(ci => ci.ProductVariant)
            .WithMany(v => v.CartItems)
            .HasForeignKey(ci => ci.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ci => ci.GiftBundle)
            .WithMany(gb => gb.CartItems)
            .HasForeignKey(ci => ci.GiftBundleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Matching query filter: keep regular items hidden when their variant is inactive.
        builder.HasQueryFilter(ci => ci.ProductVariantId == null || ci.ProductVariant!.IsActive);
    }
}
