using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace ECommerce_System.ViewModels.Admin;

// ─── Used in Product/Index ───────────────────────────────────────────────────

public class ProductIndexVM
{
    public int    Id            { get; set; }
    public string Name          { get; set; } = string.Empty;
    public decimal BasePrice    { get; set; }
    public string CategoryName  { get; set; } = string.Empty;
    public bool   IsActive      { get; set; }
    public double AverageRating { get; set; }
    public int    VariantCount  { get; set; }
    public int    ImageCount    { get; set; }
    public int    TotalStock    { get; set; }
    public string? MainImageUrl { get; set; }
    public DateTime CreatedAt  { get; set; }
}

// ─── Used in Product/Create + Product/Edit ───────────────────────────────────

public class ProductFormVM
{
    public int Id { get; set; }   // 0 for Create

    [Required(ErrorMessage = "Product name is required")]
    [MaxLength(200)]
    [Display(Name = "Product Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Base price is required")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Price must be greater than zero")]
    [Display(Name = "Base Price")]
    public decimal BasePrice { get; set; }

    [Required(ErrorMessage = "Category is required")]
    [Display(Name = "Category")]
    public int CategoryId { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    /// <summary>Populated by controller for the dropdown.</summary>
    public IEnumerable<SelectListItem> Categories { get; set; } = [];
}

// ─── Used in Product/Details ─────────────────────────────────────────────────

public class ProductDetailsVM
{
    public int     Id            { get; set; }
    public string  Name          { get; set; } = string.Empty;
    public string? Description   { get; set; }
    public decimal BasePrice     { get; set; }
    public string  CategoryName  { get; set; } = string.Empty;
    public bool    IsActive      { get; set; }
    public double  AverageRating { get; set; }
    public DateTime CreatedAt    { get; set; }
    public DateTime UpdatedAt    { get; set; }

    public IList<ProductVariantVM>  Variants { get; set; } = [];
    public IList<ProductImageVM>    Images   { get; set; } = [];

    // Summary stats
    public int     TotalStock       => Variants.Sum(v => v.Stock);
    public decimal InventoryValue   => Variants.Sum(v => v.Stock * v.Price);
    public int     LowStockCount    => Variants.Count(v => v.IsActive && v.Stock is > 0 and < 10);
    public int     OutOfStockCount  => Variants.Count(v => v.IsActive && v.Stock == 0);
}
