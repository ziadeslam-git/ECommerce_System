using ECommerce_System.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce_System.Data.EntityConfigurations;

public class GiftBundleProductConfiguration : IEntityTypeConfiguration<GiftBundleProduct>
{
    public void Configure(EntityTypeBuilder<GiftBundleProduct> builder)
    {
        builder.HasKey(item => item.Id);

        builder.Property(item => item.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.HasIndex(item => new { item.GiftBundleId, item.ProductId })
            .IsUnique();

        builder.HasOne(item => item.Product)
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
