using ECommerce_System.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce_System.Data.EntityConfigurations;

public class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> builder)
    {
        builder.HasKey(w => w.Id);

        builder.Property(w => w.AddedAt)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        // Unique: one entry per product per user
        builder.HasIndex(w => new { w.UserId, w.ProductId }).IsUnique();

        // WishlistItem → ApplicationUser (Cascade)
        builder.HasOne(w => w.User)
            .WithMany(u => u.WishlistItems)
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
