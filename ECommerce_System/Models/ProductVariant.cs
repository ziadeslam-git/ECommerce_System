namespace ECommerce_System.Models;

public class ProductVariant
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Size { get; set; } = string.Empty;   // XS/S/M/L/XL/XXL
    public string Color { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public decimal Price { get; set; }                 // Can override BasePrice
    public int Stock { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    // Optimistic concurrency token — prevents race conditions on Stock updates
    public byte[] RowVersion { get; set; } = [];

    // Navigation
    public Product Product { get; set; } = null!;
    public ICollection<CartItem> CartItems { get; set; } = [];
    public ICollection<OrderItem> OrderItems { get; set; } = [];
}
