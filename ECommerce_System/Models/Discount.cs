namespace ECommerce_System.Models;

public class Discount
{
    public int Id { get; set; }
    public string CouponCode { get; set; } = string.Empty;  // e.g. SUMMER25
    public string Type { get; set; } = string.Empty;        // SD.Discount_*
    public decimal Value { get; set; }                       // 25 = 25% or $25 off
    public decimal? MinimumOrderAmount { get; set; }
    public int? UsageLimit { get; set; }                     // NULL = unlimited
    public int UsageCount { get; set; } = 0;
    public DateTime? ExpiresAt { get; set; }                 // NULL = no expiry
    public bool IsActive { get; set; } = true;
}
