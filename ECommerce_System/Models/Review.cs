namespace ECommerce_System.Models;

public class Review
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public int Rating { get; set; }                  // 1–5
    public string? Comment { get; set; }
    public bool IsApproved { get; set; } = false;    // Admin must approve
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ApplicationUser User { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
