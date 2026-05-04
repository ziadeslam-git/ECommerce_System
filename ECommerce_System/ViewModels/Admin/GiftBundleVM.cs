using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ECommerce_System.ViewModels.Admin;

public class GiftBundleIndexVM
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal BundlePrice { get; set; }
    public decimal OriginalTotal { get; set; }
    public bool IsActive { get; set; }
    public bool IsFeatured { get; set; }
    public int ProductCount { get; set; }
    public List<GiftBundleProductPreviewVM> Products { get; set; } = [];
}

public class GiftBundleFormVM
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "Bundle Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Short Description")]
    [StringLength(600)]
    public string? Description { get; set; }

    [Range(0.01, 999999)]
    [Display(Name = "Offer Price")]
    public decimal BundlePrice { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Feature on Home")]
    public bool IsFeatured { get; set; }

    public List<int> SelectedProductIds { get; set; } = [];
    public List<SelectListItem> ProductOptions { get; set; } = [];
    public List<GiftBundleProductPickerVM> ProductCards { get; set; } = [];
    public decimal OriginalTotal { get; set; }
}

public class GiftBundleProductPreviewVM
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
}

public class GiftBundleProductPickerVM
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal Price { get; set; }
    public bool HasAvailableVariant { get; set; }
    public bool Selected { get; set; }
}
