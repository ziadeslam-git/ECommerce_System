using ECommerce_System.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce_System.Data.EntityConfigurations;

public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Size)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(v => v.Color)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(v => v.SKU)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(v => v.Price)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(v => v.Stock)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(v => v.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // Optimistic Concurrency — RowVersion
        builder.Property(v => v.RowVersion)
            .IsRowVersion()
            .IsRequired();

        // Stock CHECK ≥ 0 via check constraint
        builder.ToTable(t => t.HasCheckConstraint("CK_ProductVariants_Stock", "[Stock] >= 0"));

        // Indexes
        builder.HasIndex(v => v.SKU).IsUnique();
        builder.HasIndex(v => v.ProductId);

        // Composite unique: (ProductId, Size, Color)
        builder.HasIndex(v => new { v.ProductId, v.Size, v.Color }).IsUnique();
    }
}
