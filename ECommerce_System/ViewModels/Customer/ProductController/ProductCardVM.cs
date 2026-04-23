namespace ECommerce_System.ViewModels.Customer.ProductController
{
    public class ProductCardVM
    {
        public class ProductCardVM
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal BasePrice { get; set; }
            public decimal? MinVariantPrice { get; set; }  
            public double AverageRating { get; set; }
            public int ReviewCount { get; set; }
            public string? MainImageUrl { get; set; }
            public string? CategoryName { get; set; }
            public bool IsInWishlist { get; set; }
            public bool HasStock { get; set; }  
        }

    }
}
