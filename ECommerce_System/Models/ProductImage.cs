namespace ECommerce_System.Models;

public class ProductImage
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;   // Cloudinary URL
    public string PublicId { get; set; } = string.Empty;   // Cloudinary Public ID
    public bool IsMain { get; set; } = false;
    public int DisplayOrder { get; set; } = 0;

    // Navigation
    public Product Product { get; set; } = null!;
}
