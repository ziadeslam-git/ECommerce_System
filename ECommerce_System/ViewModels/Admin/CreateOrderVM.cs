using System.ComponentModel.DataAnnotations;
using ECommerce_System.Utilities;

namespace ECommerce_System.ViewModels.Admin;

// ────────────────────────────────────────────────────────────
//  CreateOrderVM  – Admin order creation form
// ────────────────────────────────────────────────────────────
public class CreateOrderVM
{
    [Required(ErrorMessage = "Customer is required.")]
    [Display(Name = "Customer")]
    public string UserId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Address is required.")]
    [Display(Name = "Shipping Address")]
    public int AddressId { get; set; }

    [Display(Name = "Order Status")]
    public string Status { get; set; } = SD.Status_Pending;

    [Display(Name = "Payment Status")]
    public string PaymentStatus { get; set; } = SD.Payment_Unpaid;

    [Required]
    [Range(0, 9999999, ErrorMessage = "Subtotal must be a positive number.")]
    [Display(Name = "Subtotal (EGP)")]
    public decimal Subtotal { get; set; }

    [Range(0, 9999999, ErrorMessage = "Discount must be a positive number.")]
    [Display(Name = "Discount Amount (EGP)")]
    public decimal DiscountAmount { get; set; } = 0;

    [StringLength(50)]
    [Display(Name = "Coupon Code")]
    public string? CouponCode { get; set; }
}
