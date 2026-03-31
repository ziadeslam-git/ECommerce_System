namespace ECommerce_System.Models;

public class ProductVariantImage
{
    public int Id { get; set; }
    public int ProductVariantId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string PublicId { get; set; } = string.Empty;
    public bool IsMain { get; set; } = false;

    // Navigation
    public ProductVariant ProductVariant { get; set; } = null!;
}
