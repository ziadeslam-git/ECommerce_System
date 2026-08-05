using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Resources;
using ECommerce_System.Services.Interfaces;
using ECommerce_System.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ECommerce_System.Services;

/// <summary>
/// Validates and resolves coupon codes against all business rules.
/// Extracted from the private ResolveCouponAsync in Customer/OrdersController (~80 lines).
/// Now reusable by CheckoutService, Admin discount previews, and future API.
/// </summary>
public sealed class DiscountService(
    IUnitOfWork                          uow,
    IStringLocalizer<SharedResource>     localizer,
    ILogger<DiscountService>             logger)
    : IDiscountService
{
    public async Task<CouponResult> ResolveCouponAsync(string userId, string? couponCode, decimal subtotal)
    {
        if (string.IsNullOrWhiteSpace(couponCode))
            return new CouponResult(null, 0m, null, null);

        var normalizedCode = couponCode.Trim().ToUpperInvariant();

        var coupon = await uow.Discounts.FindAsync(
            d => d.CouponCode.ToUpper() == normalizedCode && d.IsActive);

        if (coupon is null)
        {
            logger.LogDebug("Coupon '{Code}' not found or inactive. UserId={UserId}.", normalizedCode, userId);
            return new CouponResult(null, 0m,
                localizer["CouponInvalidOrDeactivated", couponCode].Value,
                normalizedCode);
        }

        if (coupon.ExpiresAt.HasValue && coupon.ExpiresAt.Value < DateTime.UtcNow)
            return new CouponResult(null, 0m,
                localizer["CouponHasExpired", coupon.CouponCode].Value,
                coupon.CouponCode);

        if (coupon.UsageLimit.HasValue && coupon.UsageCount >= coupon.UsageLimit.Value)
            return new CouponResult(null, 0m,
                localizer["CouponNamedUsageLimitReached", coupon.CouponCode].Value,
                coupon.CouponCode);

        var alreadyUsed = await uow.Orders
            .Query()
            .AnyAsync(o => o.UserId == userId && o.CouponCode == coupon.CouponCode);

        if (alreadyUsed)
            return new CouponResult(null, 0m,
                localizer["CouponAlreadyUsed", coupon.CouponCode].Value,
                coupon.CouponCode);

        if (coupon.MinimumOrderAmount.HasValue && subtotal < coupon.MinimumOrderAmount.Value)
            return new CouponResult(null, 0m,
                localizer["CouponMinimumOrderRequiredDetailed",
                    coupon.MinimumOrderAmount.Value.ToString("C", System.Globalization.CultureInfo.CurrentCulture),
                    subtotal.ToString("C", System.Globalization.CultureInfo.CurrentCulture)].Value,
                coupon.CouponCode);

        var discountAmount = CalculateDiscount(coupon, subtotal);

        logger.LogInformation(
            "Coupon '{Code}' resolved. Discount={Discount:C}. UserId={UserId}.",
            coupon.CouponCode, discountAmount, userId);

        return new CouponResult(coupon, discountAmount, null, coupon.CouponCode);
    }

    public decimal CalculateDiscount(Discount coupon, decimal subtotal)
    {
        var amount = coupon.Type == SD.Discount_Percentage
            ? subtotal * (coupon.Value / 100m)
            : coupon.Value;

        return Math.Min(amount, subtotal); // never discount more than subtotal
    }
}
