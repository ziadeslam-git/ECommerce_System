namespace ECommerce_System.ViewModels.Customer.ProductController
{
    public class ProductIndexCustomerVM
    {
        public IEnumerable<ProductCardVM> Products { get; set; } = [];
        public IEnumerable<SelectListItem> Categories { get; set; } = [];
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public int TotalCount { get; set; }
        public string? SearchQuery { get; set; }
        public int? SelectedCategoryId { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public string? Sort { get; set; }
    }
}
