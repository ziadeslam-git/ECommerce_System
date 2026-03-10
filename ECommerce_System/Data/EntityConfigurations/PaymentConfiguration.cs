using ECommerce_System.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce_System.Data.EntityConfigurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Amount)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.Provider)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.TransactionId)
            .HasMaxLength(200);

        builder.Property(p => p.Status)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(p => p.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        // One payment per order (unique FK)
        builder.HasIndex(p => p.OrderId).IsUnique();
    }
}
