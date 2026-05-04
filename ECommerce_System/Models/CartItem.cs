namespace ECommerce_System.Models;

public class CartItem
{
    public int Id { get; set; }
    public int CartId { get; set; }
    public int? ProductVariantId { get; set; }
    public int? GiftBundleId { get; set; }
    public int Quantity { get; set; }
    public decimal PriceSnapshot { get; set; }  // Price at time of adding
    public string? GiftBundleTitle { get; set; }
    public decimal? GiftBundleOriginalTotal { get; set; }
    public string? GiftBundleItemsJson { get; set; }

    // Navigation
    public Cart Cart { get; set; } = null!;
    public ProductVariant? ProductVariant { get; set; }
    public GiftBundle? GiftBundle { get; set; }
}
