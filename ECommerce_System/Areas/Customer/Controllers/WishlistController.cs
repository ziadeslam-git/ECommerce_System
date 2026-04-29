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
public class WishlistController : Controller
{
    private readonly IUnitOfWork _uow;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public WishlistController(IUnitOfWork uow, IStringLocalizer<SharedResource> localizer)
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

        var wishlistItems = await _uow.WishlistItems.FindAllAsync(
            w => w.UserId == userId,
            "Product,Product.Images,Product.Variants"
        );

        var vm = new WishlistIndexVM
        {
            Items = wishlistItems.Select(w => new WishlistItemCustomerVM
            {
                WishlistItemId = w.Id,
                ProductId      = w.ProductId,
                ProductName    = w.Product?.Name ?? string.Empty,
                BasePrice      = w.Product?.BasePrice ?? 0m,
                ImageUrl       = w.Product?.Images?.FirstOrDefault(i => i.IsMain)?.ImageUrl
                                 ?? w.Product?.Images?.FirstOrDefault()?.ImageUrl,
                AverageRating  = w.Product?.AverageRating ?? 0
            }).ToList()
        };

        return View(vm);
    }

    // ─── TOGGLE (JSON) ────────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int productId, string? returnUrl = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var existingItem = await _uow.WishlistItems.FindAsync(w => w.UserId == userId && w.ProductId == productId);

        if (existingItem != null)
        {
            _uow.WishlistItems.Remove(existingItem);
            await _uow.SaveAsync();
            return BuildToggleResponse(true, false, _localizer["RemovedFromWishlist"], returnUrl);
        }
        else
        {
            var newItem = new WishlistItem
            {
                UserId = userId,
                ProductId = productId,
                AddedAt = DateTime.UtcNow
            };
            await _uow.WishlistItems.AddAsync(newItem);
            await _uow.SaveAsync();
            return BuildToggleResponse(true, true, _localizer["AddedToWishlist"], returnUrl);
        }
    }

    // ─── MOVE TO CART ─────────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveToCart(int productId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var wishlistItem = await _uow.WishlistItems.FindAsync(w => w.UserId == userId && w.ProductId == productId);

        if (wishlistItem == null)
            return RedirectToAction(nameof(Index));

        var product = await _uow.Products.FindAsync(p => p.Id == productId, "Variants");
        if (product == null)
        {
            TempData["error"] = _localizer["ProductNotFound"].Value;
            return RedirectToAction(nameof(Index));
        }

        var activeVariant = product.Variants.FirstOrDefault(v => v.IsActive && v.Stock > 0);
        if (activeVariant == null)
        {
            TempData["error"] = _localizer["NoActiveVariantAvailable"].Value;
            return RedirectToAction(nameof(Index));
        }

        // Add to cart logic
        var cart = await _uow.Carts.GetCartByUserIdAsync(userId);
        if (cart == null)
        {
            cart = new Cart { UserId = userId };
            await _uow.Carts.AddAsync(cart);
            await _uow.SaveAsync();
        }

        var cartItem = cart.Items.FirstOrDefault(i => i.ProductVariantId == activeVariant.Id);

        if (cartItem != null)
        {
            if (cartItem.Quantity + 1 > activeVariant.Stock)
            {
                TempData["error"] = _localizer["CannotAddMoreToCartMaxStock", activeVariant.Stock].Value;
                return RedirectToAction(nameof(Index));
            }
            cartItem.Quantity += 1;
            _uow.CartItems.Update(cartItem);
        }
        else
        {
            cartItem = new CartItem
            {
                CartId = cart.Id,
                ProductVariantId = activeVariant.Id,
                Quantity = 1,
                PriceSnapshot = activeVariant.Price
            };
            await _uow.CartItems.AddAsync(cartItem);
        }

        // Remove from wishlist
        _uow.WishlistItems.Remove(wishlistItem);
        
        await _uow.SaveAsync();

        TempData["success"] = _localizer["ItemMovedToCartSuccessfully"].Value;
        return RedirectToAction("Index", "Cart", new { area = "Customer" });
    }

    private IActionResult BuildToggleResponse(bool success, bool isInWishlist, string message, string? returnUrl)
    {
        if (IsAjaxRequest())
        {
            return Json(new { success, isInWishlist, message });
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
}
