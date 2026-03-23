using System.ComponentModel.DataAnnotations;
using ECommerce_System.Utilities;

namespace ECommerce_System.ViewModels.Admin;

// ────────────────────────────────────────────────────────────
//  CreateOrderVM  – Admin order creation form
// ────────────────────────────────────────────────────────────
public class CreateOrderVM
{
    [Required(ErrorMessage = "Customer Name is required.")]
    [StringLength(100)]
    [Display(Name = "Customer Name")]
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Customer Phone is required.")]
    [Phone]
    [StringLength(20)]
    [Display(Name = "Customer Phone")]
    public string CustomerPhone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Shipping Address is required.")]
    [StringLength(200)]
    [Display(Name = "Shipping Address")]
    public string ShippingAddress { get; set; } = string.Empty;

    [Display(Name = "Order Status")]
    public string Status { get; set; } = SD.Status_Pending;

    [Display(Name = "Payment Status")]
    public string PaymentStatus { get; set; } = SD.Payment_Unpaid;

    [Display(Name = "Subtotal (EGP)")]
    // Subtotal is recalculated securely on the backend, this is just for form display/submit
    public decimal Subtotal { get; set; }

    [Display(Name = "Discount Amount (EGP)")]
    // Expected to be calculated securely on the backend based on the coupon
    public decimal DiscountAmount { get; set; } = 0;

    [StringLength(50)]
    [Display(Name = "Coupon Code")]
    public string? CouponCode { get; set; }

    // Selected products for the order
    public List<OrderItemSubmitVM> Items { get; set; } = [];
}

// ────────────────────────────────────────────────────────────
//  OrderItemSubmitVM  – one line item submitted via form
// ────────────────────────────────────────────────────────────
public class OrderItemSubmitVM
{
    [Required]
    public int ProductVariantId { get; set; }

    [Required]
    [Range(1, 1000, ErrorMessage = "Quantity must be between 1 and 1000.")]
    public int Quantity { get; set; }
}
