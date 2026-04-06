namespace ECommerce_System.ViewModels.Admin;

public class UserAdminVM
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    public IList<string> Roles { get; set; } = new List<string>();
    public int TotalOrders { get; set; }
    public DateTime? LastOrderDate { get; set; }

    // BUG #2: Additional stats for Details page
    public decimal TotalSpent { get; set; }
    public int TotalReviews { get; set; }
    public List<OrderSummaryForUserVM> RecentOrders { get; set; } = new();

    // For UI
    public string StatusBadge => IsActive ? "Active" : "Inactive";
    public string StatusBadgeColor => IsActive ? "emerald" : "rose";
}

public class OrderSummaryForUserVM
{
    public int Id { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
