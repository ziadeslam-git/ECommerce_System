using ECommerce_System.Common;
using ECommerce_System.Models;

namespace ECommerce_System.Services.Interfaces;

/// <summary>
/// Validates and applies discount coupons.
/// Extracted from the 80-line ResolveCouponAsync private method in Customer/OrdersController.
/// Centralizes coupon logic so it can be reused in Admin discount previews too.
/// </summary>
public interface IDiscountService
{
    /// <summary>
    /// Validates a coupon code against all business rules:
    ///   - exists and is active
    ///   - not expired
    ///   - usage limit not exceeded
    ///   - not already used by this customer
    ///   - minimum order amount met
    /// Returns the applied discount amount, or an error message.
    /// </summary>
    Task<CouponResult> ResolveCouponAsync(string userId, string? couponCode, decimal subtotal);

    /// <summary>Calculates the discount amount for a given coupon and subtotal (Percentage or FixedAmount).</summary>
    decimal CalculateDiscount(Discount coupon, decimal subtotal);
}
