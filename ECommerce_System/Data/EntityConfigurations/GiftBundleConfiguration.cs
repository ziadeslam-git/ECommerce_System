using ECommerce_System.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce_System.Data.EntityConfigurations;

public class GiftBundleConfiguration : IEntityTypeConfiguration<GiftBundle>
{
    public void Configure(EntityTypeBuilder<GiftBundle> builder)
    {
        builder.HasKey(gb => gb.Id);

        builder.Property(gb => gb.Name)
            .IsRequired()
            .HasMaxLength(160);

        builder.Property(gb => gb.Description)
            .HasMaxLength(600);

        builder.Property(gb => gb.BundlePrice)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(gb => gb.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(gb => gb.IsFeatured)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(gb => gb.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(gb => gb.UpdatedAt)
            .IsRequired();

        builder.HasIndex(gb => gb.IsActive);
        builder.HasIndex(gb => gb.IsFeatured);

        builder.HasMany(gb => gb.Items)
            .WithOne(item => item.GiftBundle)
            .HasForeignKey(item => item.GiftBundleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
