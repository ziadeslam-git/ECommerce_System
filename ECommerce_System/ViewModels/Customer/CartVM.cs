namespace ECommerce_System.ViewModels.Customer;

public class CartIndexVM
{
    public List<CartItemVM> Items { get; set; } = new();

    public decimal Subtotal => Items.Sum(i => i.Subtotal);

    public decimal DiscountAmount { get; set; }

    public string? AppliedCouponCode { get; set; }

    public decimal Total => Subtotal - DiscountAmount;

    public int TotalItemCount => Items.Sum(i => i.Quantity);
}

public class CartItemVM
{
    public int CartItemId { get; set; }

    public int ProductVariantId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string Size { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    public decimal UnitPrice { get; set; }  // PriceSnapshot من DB

    public int Quantity { get; set; }

    public decimal Subtotal => UnitPrice * Quantity;

    public int MaxStock { get; set; }
}