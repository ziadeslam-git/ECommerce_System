using ECommerce_System.Common;
using ECommerce_System.Models;
using ECommerce_System.ViewModels.Customer;

namespace ECommerce_System.Services.Interfaces;

/// <summary>
/// Handles all checkout/order-placement business logic extracted from Customer/OrdersController.
/// Includes: address management, coupon resolution, Stripe session creation,
/// cart-to-order finalization, and stock deduction.
/// </summary>
public interface ICheckoutService
{
    // ── Address Management ────────────────────────────────────────────────────
    Task<Result<Address>> SaveNewAddressAsync(string userId, CheckoutVM vm);
    Task<Result<bool>>    SetDefaultAddressAsync(string userId, int addressId);
    Task<Result<bool>>    DeleteAddressAsync(string userId, int addressId);

    // ── Coupon ────────────────────────────────────────────────────────────────
    Task<CouponResult> ResolveCouponAsync(string userId, string? couponCode, decimal subtotal);

    // ── Checkout ViewModel ────────────────────────────────────────────────────
    Task<CheckoutVM> BuildCheckoutViewModelAsync(string userId, CheckoutVM? request);

    // ── Order Placement ───────────────────────────────────────────────────────
    /// <summary>
    /// Validates cart + address + coupon, then either creates Stripe session (credit card)
    /// or finalizes a cash-on-delivery order directly.
    /// Returns the Stripe redirect URL on credit card, or null on COD success.
    /// </summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string userId, CheckoutVM vm, string baseUrl, string successUrl, string cancelUrl);

    /// <summary>
    /// Handles Stripe StripeSuccess callback — verifies payment, finalizes the order,
    /// deducts stock, clears the cart, and sends the confirmation email.
    /// </summary>
    Task<Result<Order>> FinalizeStripeOrderAsync(string userId, string sessionId);

    /// <summary>
    /// Core order creation: deducts stock, creates Order + OrderItems + Payment,
    /// clears cart, increments coupon usage, sends email.
    /// Called both from PlaceOrderAsync (COD) and FinalizeStripeOrderAsync.
    /// </summary>
    Task<Order> FinalizeOrderAsync(
        string   userId,
        int      addressId,
        Cart     cart,
        decimal  subtotal,
        decimal  discountAmount,
        Discount? appliedCoupon,
        string   paymentStatus,
        string?  paymentProvider,
        string?  transactionId,
        string?  couponCodeOverride = null);
}

// ── Result value objects ──────────────────────────────────────────────────────

public sealed record CouponResult(
    Discount? Coupon,
    decimal   DiscountAmount,
    string?   ErrorMessage,
    string?   AppliedCode)
{
    public bool IsValid => ErrorMessage is null;
}

public sealed record PlaceOrderResult(
    bool    Success,
    string? StripeRedirectUrl,
    int?    OrderId,
    string? ErrorMessage)
{
    public bool IsStripeRedirect => StripeRedirectUrl is not null;
}
