namespace ECommerce_System.ViewModels.Admin;

public class TopProductVM
{
    public string ProductName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int TotalSold { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
}
