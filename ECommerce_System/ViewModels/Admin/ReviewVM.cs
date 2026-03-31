namespace ECommerce_System.ViewModels.Admin;

public class ReviewVM
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;

    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;

    public int Rating { get; set; }
    public string? Comment { get; set; }
    public bool IsApproved { get; set; }
    public bool IsRejected { get; set; }
    public DateTime CreatedAt { get; set; }

    // For UI
    public string StatusBadge => IsApproved ? "Approved" : (IsRejected ? "Rejected" : "Pending");
    public string StatusBadgeColor => IsApproved ? "emerald" : (IsRejected ? "rose" : "amber");
}
