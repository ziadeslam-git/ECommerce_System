namespace ECommerce_System.ViewModels.Customer;

public class HomeIndexVM
{
    public List<ProductCardVM> FeaturedProducts { get; set; } = [];
    public List<CategoryCardVM> Categories { get; set; } = [];
}

public class ProductCardVM
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public decimal MinVariantPrice { get; set; }
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

