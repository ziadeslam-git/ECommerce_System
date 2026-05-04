using ECommerce_System.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ECommerce_System.ViewModels.Customer.ProductController;

public class ProductCardVM
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public decimal? MinVariantPrice { get; set; }
    public int? DefaultVariantId { get; set; }
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public string? MainImageUrl { get; set; }
    public string? CategoryName { get; set; }
    public bool IsInWishlist { get; set; }
    public bool HasStock { get; set; }
    public int AvailableStock { get; set; }
}

public class ProductIndexCustomerVM
{
    public IEnumerable<ProductCardVM> Products { get; set; } = [];
    public IEnumerable<SelectListItem> Categories { get; set; } = [];
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public int PageSize { get; set; }
    public string? SearchQuery { get; set; }
    public int? SelectedCategoryId { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? Sort { get; set; }

    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;
    public int ShowingFrom => TotalCount == 0 ? 0 : ((CurrentPage - 1) * PageSize) + 1;
    public int ShowingTo => TotalCount == 0 ? 0 : Math.Min(CurrentPage * PageSize, TotalCount);
}

public class ProductDetailsCustomerVM
{
    public Product Product { get; set; } = null!;
    public List<Review> Reviews { get; set; } = [];
    public List<Product> RelatedProducts { get; set; } = [];
    public bool IsInWishlist { get; set; }
}

// Preserve the existing Razor view model name while consolidating these VMs in one file.
public class ProductDetailsVM : ProductDetailsCustomerVM
{
}
