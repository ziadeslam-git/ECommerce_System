namespace ECommerce_System.ViewModels.Admin;

public class UserAdminVM
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    public IList<string> Roles { get; set; } = new List<string>();
    public int TotalOrders { get; set; }  // count only — no order details
    public DateTime? LastOrderDate { get; set; }

    // For UI
    public string StatusBadge => IsActive ? "Active" : "Inactive";
    public string StatusBadgeColor => IsActive ? "emerald" : "rose";
}
