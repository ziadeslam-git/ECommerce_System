using ECommerce_System.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce_System.Data.EntityConfigurations;

public class DiscountConfiguration : IEntityTypeConfiguration<Discount>
{
    public void Configure(EntityTypeBuilder<Discount> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.CouponCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(d => d.CouponCode).IsUnique();

        builder.Property(d => d.Type)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(d => d.Value)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(d => d.MinimumOrderAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(d => d.UsageCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(d => d.IsActive)
            .IsRequired()
            .HasDefaultValue(true);
    }
}
