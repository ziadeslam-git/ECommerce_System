namespace ECommerce_System.ViewModels.Customer;

public class HomeIndexVM
{
    public List<ProductCardVM> FeaturedProducts { get; set; } = [];
    public List<CategoryCardVM> Categories { get; set; } = [];
    public GiftBundleHomeVM? FeaturedGiftBundle { get; set; }
}

public class ProductCardVM
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public decimal MinVariantPrice { get; set; }
    public int? DefaultVariantId { get; set; }
    public double AverageRating { get; set; }
    public string? MainImageUrl { get; set; }
    public string? CategoryName { get; set; }
}

public class CategoryCardVM
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int ProductCount { get; set; }
}

public class GiftBundleHomeVM
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal BundlePrice { get; set; }
    public decimal OriginalTotal { get; set; }
    public List<GiftBundleHomeItemVM> Items { get; set; } = [];
}

public class GiftBundleHomeItemVM
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? MainImageUrl { get; set; }
}

