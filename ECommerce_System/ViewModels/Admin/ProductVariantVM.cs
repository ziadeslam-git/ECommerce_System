using System.ComponentModel.DataAnnotations;

namespace ECommerce_System.ViewModels.Admin
{
    public class ProductVariantVM
    {
        public int Id { get; set; }

        public int ProductId { get; set; }

        [Required(ErrorMessage = "Size is required")]
        [StringLength(10)]
        public string Size { get; set; } = string.Empty;  // XS/S/M/L/XL/XXL

        [Required(ErrorMessage = "Color is required")]
        [StringLength(50)]
        public string Color { get; set; } = string.Empty;

        [Required(ErrorMessage = "SKU is required")]
        [StringLength(50)]
        public string SKU { get; set; } = string.Empty;

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Price must be greater than 0")]
        public decimal Price { get; set; }

        [Required]
        [Range(0, int.MaxValue, ErrorMessage = "Stock must be 0 or more")]
        public int Stock { get; set; }

        public bool IsActive { get; set; } = true;

        public byte[]? RowVersion { get; set; }  // Optimistic Concurrency

        public string? ProductName { get; set; }

        public List<ProductVariantImageVM> Images { get; set; } = new();

        public List<Microsoft.AspNetCore.Http.IFormFile>? ImageFiles { get; set; }
        
        [Display(Name = "Set one image as Main Product Image")]
        public bool SetAsMainProductImage { get; set; }
    }
    
    public class ProductVariantImageVM
    {
        public int Id { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public string PublicId { get; set; } = string.Empty;
        public bool IsMain { get; set; }
    }
}
