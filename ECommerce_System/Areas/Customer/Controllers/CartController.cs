using System.Security.Claims;
using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Customer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce_System.Areas.Customer.Controllers;

[Area("Customer")]
[Authorize(Roles = SD.Role_Customer)]
public class CartController : Controller
{
    private readonly IUnitOfWork _uow;

    public CartController(IUnitOfWork uow)
    {
        _uow = uow;
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
    public async Task<IActionResult> AddToCart(int productVariantId, int quantity)
    {
        if (quantity < 1)
            return Json(new { success = false, message = "Quantity must be at least 1." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var variant = await _uow.ProductVariants.FindAsync(v => v.Id == productVariantId, "Product");
        if (variant == null || !variant.IsActive || variant.Stock < quantity)
        {
            return Json(new { success = false, message = "Item is unavailable or insufficient stock." });
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
                return Json(new { success = false, message = $"Cannot add more. Max stock available: {variant.Stock}." });
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

        return Json(new { success = true, message = "Added to cart successfully.", cartCount });
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
            return Json(new { success = false, message = "Invalid request." });
        }

        if (quantity < 1 || quantity > cartItem.ProductVariant.Stock)
        {
            return Json(new { success = false, message = $"Invalid quantity. Available stock: {cartItem.ProductVariant.Stock}." });
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
            return Json(new { success = false, message = "Please enter a coupon code." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var discount = await _uow.Discounts.FindAsync(d => d.CouponCode.ToLower() == couponCode.ToLower());

        if (discount == null || !discount.IsActive)
            return Json(new { success = false, message = "Invalid or inactive coupon." });

        if (discount.ExpiresAt.HasValue && discount.ExpiresAt.Value < DateTime.UtcNow)
            return Json(new { success = false, message = "This coupon has expired." });

        if (discount.UsageLimit.HasValue && discount.UsageCount >= discount.UsageLimit.Value)
            return Json(new { success = false, message = "This coupon's usage limit has been reached." });

        var cart = await _uow.Carts.GetCartByUserIdAsync(userId);
        if (cart == null || !cart.Items.Any())
            return Json(new { success = false, message = "Your cart is empty." });

        decimal subtotal = cart.Items.Sum(i => i.Quantity * i.PriceSnapshot);

        if (discount.MinimumOrderAmount.HasValue && subtotal < discount.MinimumOrderAmount.Value)
            return Json(new { success = false, message = $"Minimum order amount of ${discount.MinimumOrderAmount.Value:0.00} required." });

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
            message = "Coupon applied successfully!" 
        });
    }
}
