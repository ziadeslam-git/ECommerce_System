using ECommerce_System.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce_System.Data.EntityConfigurations;

public class ProductVariantImageConfiguration : IEntityTypeConfiguration<ProductVariantImage>
{
    public void Configure(EntityTypeBuilder<ProductVariantImage> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ImageUrl)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(i => i.PublicId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(i => i.IsMain)
            .IsRequired()
            .HasDefaultValue(false);

        // One-to-many relationship mapping
        builder.HasOne(i => i.ProductVariant)
            .WithMany(v => v.Images)
            .HasForeignKey(i => i.ProductVariantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Matching query filter: hide variant images when their variant is inactive (mirrors ProductVariant filter)
        builder.HasQueryFilter(i => i.ProductVariant!.IsActive);
    }
}
