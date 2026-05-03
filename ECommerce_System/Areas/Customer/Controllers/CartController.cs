using System.Security.Claims;
using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Resources;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Customer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ECommerce_System.Areas.Customer.Controllers;

[Area("Customer")]
[Authorize(Roles = SD.Role_AdminOrCustomer)]
public class CartController : Controller
{
    private readonly IUnitOfWork _uow;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public CartController(IUnitOfWork uow, IStringLocalizer<SharedResource> localizer)
    {
        _uow = uow;
        _localizer = localizer;
    }

    // ─── INDEX ────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var cart = await _uow.Carts.GetCartByUserIdAsync(userId);
        
        var vm = new CartIndexVM();

        if (cart != null && cart.Items.Any())
        {
            vm.Items = cart.Items.Select(i => new CartItemVM
            {
                CartItemId = i.Id,
                ProductVariantId = i.ProductVariantId,
                ProductName = i.ProductVariant.Product.Name,
                Size = i.ProductVariant.Size,
                Color = i.ProductVariant.Color,
                ImageUrl = i.ProductVariant.Product.Images.FirstOrDefault(img => img.IsMain)?.ImageUrl
                           ?? i.ProductVariant.Product.Images.FirstOrDefault()?.ImageUrl,
                UnitPrice = i.PriceSnapshot,
                Quantity = i.Quantity,
                MaxStock = i.ProductVariant.Stock
            }).ToList();
        }

        return View(vm);
    }

    // ─── ADD TO CART (JSON) ───────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToCart(int productVariantId, int quantity, string? returnUrl = null)
    {
        if (quantity < 1)
            return BuildAddToCartResponse(false, _localizer["QuantityMustBeAtLeastOne"], null, returnUrl);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var variant = await _uow.ProductVariants.FindAsync(v => v.Id == productVariantId, "Product");
        if (variant == null || !variant.IsActive)
        {
            return BuildAddToCartResponse(false, _localizer["VariantUnavailableRightNow"], null, returnUrl);
        }

        if (variant.Stock <= 0)
        {
            return BuildAddToCartResponse(false, _localizer["OutOfStockRestockSoon"], null, returnUrl);
        }

        if (variant.Stock < quantity)
        {
            return BuildAddToCartResponse(false, _localizer["OnlyLeftInStock", variant.Stock], null, returnUrl);
        }

        var cart = await _uow.Carts.GetCartByUserIdAsync(userId);
        if (cart == null)
        {
            cart = new Cart { UserId = userId };
            await _uow.Carts.AddAsync(cart);
            await _uow.SaveAsync();
        }

        var cartItem = cart.Items.FirstOrDefault(i => i.ProductVariantId == productVariantId);

        if (cartItem != null)
        {
            if (cartItem.Quantity + quantity > variant.Stock)
            {
                return BuildAddToCartResponse(false, _localizer["AlreadyHaveInCartStockLimit", variant.Stock, cartItem.Quantity], null, returnUrl);
            }
            cartItem.Quantity += quantity;
            _uow.CartItems.Update(cartItem);
        }
        else
        {
            cartItem = new CartItem
            {
                CartId = cart.Id,
                ProductVariantId = productVariantId,
                Quantity = quantity,
                PriceSnapshot = variant.Price
            };
            await _uow.CartItems.AddAsync(cartItem);
        }

        await _uow.SaveAsync();

        // Refresh cart count
        var updatedCart = await _uow.Carts.GetCartByUserIdAsync(userId);
        var cartCount = updatedCart?.Items.Sum(i => i.Quantity) ?? 0;

        return BuildAddToCartResponse(true, _localizer["AddedToCartSuccessfully"], cartCount, returnUrl);
    }

    // ─── UPDATE QUANTITY (JSON) ───────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateQuantity(int cartItemId, int quantity)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var cartItem = await _uow.CartItems.FindAsync(i => i.Id == cartItemId, "Cart,ProductVariant");

        if (cartItem == null || cartItem.Cart.UserId != userId)
        {
            return Json(new { success = false, message = _localizer["InvalidRequest"].Value });
        }

        if (quantity < 1 || quantity > cartItem.ProductVariant.Stock)
        {
            return Json(new { success = false, message = _localizer["InvalidQuantityAvailableStock", cartItem.ProductVariant.Stock].Value });
        }

        cartItem.Quantity = quantity;
        _uow.CartItems.Update(cartItem);
        await _uow.SaveAsync();

        var updatedCart = await _uow.Carts.GetCartByUserIdAsync(userId);
        decimal cartTotal = updatedCart?.Items.Sum(i => i.Quantity * i.PriceSnapshot) ?? 0;

        return Json(new 
        { 
            success = true, 
            itemSubtotal = cartItem.Quantity * cartItem.PriceSnapshot, 
            cartTotal = cartTotal 
        });
    }

    // ─── REMOVE ITEM ──────────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(int cartItemId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var cartItem = await _uow.CartItems.FindAsync(i => i.Id == cartItemId, "Cart");

        if (cartItem != null && cartItem.Cart.UserId == userId)
        {
            _uow.CartItems.Remove(cartItem);
            await _uow.SaveAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    // ─── APPLY COUPON (JSON) ──────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyCoupon(string couponCode)
    {
        if (string.IsNullOrWhiteSpace(couponCode))
            return Json(new { success = false, message = _localizer["PleaseEnterCouponCode"].Value });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var discount = await _uow.Discounts.FindAsync(d => d.CouponCode.ToLower() == couponCode.ToLower());

        if (discount == null || !discount.IsActive)
            return Json(new { success = false, message = _localizer["InvalidOrInactiveCoupon"].Value });

        if (discount.ExpiresAt.HasValue && discount.ExpiresAt.Value < DateTime.UtcNow)
            return Json(new { success = false, message = _localizer["CouponExpired"].Value });

        if (discount.UsageLimit.HasValue && discount.UsageCount >= discount.UsageLimit.Value)
            return Json(new { success = false, message = _localizer["CouponUsageLimitReached"].Value });

        var cart = await _uow.Carts.GetCartByUserIdAsync(userId);
        if (cart == null || !cart.Items.Any())
            return Json(new { success = false, message = _localizer["CartEmpty"].Value });

        decimal subtotal = cart.Items.Sum(i => i.Quantity * i.PriceSnapshot);

        if (discount.MinimumOrderAmount.HasValue && subtotal < discount.MinimumOrderAmount.Value)
            return Json(new
            {
                success = false,
                message = _localizer["MinimumOrderAmountRequired", discount.MinimumOrderAmount.Value.ToString("C", System.Globalization.CultureInfo.CurrentCulture)].Value
            });

        decimal discountAmount = 0;
        if (discount.Type == SD.Discount_Percentage)
        {
            discountAmount = subtotal * (discount.Value / 100);
        }
        else if (discount.Type == SD.Discount_FixedAmount)
        {
            discountAmount = discount.Value;
        }

        // Ensuring discount doesn't exceed subtotal
        if (discountAmount > subtotal)
            discountAmount = subtotal;

        return Json(new 
        { 
            success = true, 
            discountAmount = discountAmount, 
            message = _localizer["CouponAppliedSuccessfully"].Value
        });
    }

    private IActionResult BuildAddToCartResponse(bool success, string message, int? cartCount, string? returnUrl)
    {
        if (IsAjaxRequest())
        {
            return Json(new { success, message, cartCount });
        }

        TempData[success ? "success" : "error"] = message;
        return RedirectBackOrDefault(returnUrl);
    }

    private bool IsAjaxRequest()
    {
        return string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
    }

    private IActionResult RedirectBackOrDefault(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        var referer = Request.Headers.Referer.ToString();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri) && Url.IsLocalUrl(refererUri.PathAndQuery))
        {
            return LocalRedirect(refererUri.PathAndQuery);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearCart()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var cart = await _uow.Carts.GetCartByUserIdAsync(userId);
        if (cart is not null && cart.Items.Any())
        {
            _uow.CartItems.RemoveRange(cart.Items.ToList());
            await _uow.SaveAsync();
        }

        TempData["success"] = "Cart cleared.";
        return RedirectToAction(nameof(Index));
    }
}
