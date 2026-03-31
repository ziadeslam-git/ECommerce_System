using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ECommerce_System.ViewModels.Admin;

public class DiscountVM
{
    public int Id { get; set; }

    [Required(ErrorMessage = "This field is required.")]
    [StringLength(50)]
    [Display(Name = "Coupon Code")]
    public string CouponCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "This field is required.")]
    [Display(Name = "Discount Type")]
    public string Type { get; set; } = "Percentage"; // or "FixedAmount"

    [Required(ErrorMessage = "This field is required.")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Value must be greater than 0")]
    public decimal DiscountValue { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Minimum order cannot be negative")]
    [Display(Name = "Minimum Order Amount")]
    public decimal? MinOrderValue { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Limit must be at least 1")]
    [Display(Name = "Usage Limit (leave empty = unlimited)")]
    public int? UsageLimit { get; set; }

    // READ-ONLY — never bind from form
    [BindNever]
    public int UsageCount { get; set; }

    [Display(Name = "Start Date (optional)")]
    public DateTime? StartDate { get; set; }

    [Display(Name = "Expires At (leave empty = no expiry)")]
    public DateTime? EndDate { get; set; }

    public bool IsActive { get; set; } = true;

    // For UI display
    public bool IsExpired => EndDate.HasValue && EndDate.Value < DateTime.UtcNow;
    public bool IsLimitReached => UsageLimit.HasValue && UsageCount >= UsageLimit.Value;
}
