namespace ECommerce_System.Models;

public class GiftBundle
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal BundlePrice { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<GiftBundleProduct> Items { get; set; } = [];
    public ICollection<CartItem> CartItems { get; set; } = [];
}
