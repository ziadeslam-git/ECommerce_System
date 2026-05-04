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
            var bundleVariantStockLookup = await BuildBundleVariantStockLookupAsync(cart.Items);
            vm.Items = cart.Items
                .Select(item => MapCartItemViewModel(item, bundleVariantStockLookup))
                .ToList();
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddGiftBundleToCart(int giftBundleId, string? returnUrl = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var (bundle, snapshotItems, maxStock, errorMessage) = await PrepareGiftBundleAsync(giftBundleId);
        if (bundle == null || snapshotItems.Count == 0)
        {
            return BuildAddToCartResponse(false, errorMessage ?? "This gift bundle is not available right now.", null, returnUrl);
        }

        if (maxStock <= 0)
        {
            return BuildAddToCartResponse(false, "This gift bundle is currently out of stock.", null, returnUrl);
        }

        var cart = await _uow.Carts.GetCartByUserIdAsync(userId);
        if (cart == null)
        {
            cart = new Cart { UserId = userId };
            await _uow.Carts.AddAsync(cart);
            await _uow.SaveAsync();
        }

        var existingBundleItem = cart.Items.FirstOrDefault(item => item.GiftBundleId == giftBundleId);
        if (existingBundleItem != null)
        {
            if (existingBundleItem.Quantity + 1 > maxStock)
            {
                return BuildAddToCartResponse(false, $"Only {maxStock} bundle(s) can be added with the current stock.", null, returnUrl);
            }

            existingBundleItem.Quantity += 1;
            existingBundleItem.PriceSnapshot = bundle.BundlePrice;
            existingBundleItem.GiftBundleTitle = bundle.Name;
            existingBundleItem.GiftBundleOriginalTotal = snapshotItems.Sum(item => item.UnitPrice);
            existingBundleItem.GiftBundleItemsJson = GiftBundleSnapshotHelper.Serialize(snapshotItems);
            _uow.CartItems.Update(existingBundleItem);
        }
        else
        {
            await _uow.CartItems.AddAsync(new CartItem
            {
                CartId = cart.Id,
                GiftBundleId = bundle.Id,
                Quantity = 1,
                PriceSnapshot = bundle.BundlePrice,
                GiftBundleTitle = bundle.Name,
                GiftBundleOriginalTotal = snapshotItems.Sum(item => item.UnitPrice),
                GiftBundleItemsJson = GiftBundleSnapshotHelper.Serialize(snapshotItems)
            });
        }

        await _uow.SaveAsync();

        var updatedCart = await _uow.Carts.GetCartByUserIdAsync(userId);
        var cartCount = updatedCart?.Items.Sum(item => item.Quantity) ?? 0;

        return BuildAddToCartResponse(true, "Gift bundle added to cart successfully.", cartCount, returnUrl);
    }

    // ─── UPDATE QUANTITY (JSON) ───────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateQuantity(int cartItemId, int quantity)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var cartItem = await _uow.CartItems.FindAsync(i => i.Id == cartItemId, "Cart,ProductVariant,GiftBundle");

        if (cartItem == null || cartItem.Cart.UserId != userId)
        {
            return Json(new { success = false, message = _localizer["InvalidRequest"].Value });
        }

        var maxStock = cartItem.GiftBundleId.HasValue
            ? await GetGiftBundleMaxStockAsync(GiftBundleSnapshotHelper.Deserialize(cartItem.GiftBundleItemsJson))
            : cartItem.ProductVariant?.Stock ?? 0;

        if (quantity < 1 || quantity > maxStock)
        {
            return Json(new { success = false, message = _localizer["InvalidQuantityAvailableStock", maxStock].Value });
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

    private CartItemVM MapCartItemViewModel(CartItem cartItem, IReadOnlyDictionary<int, int> bundleVariantStockLookup)
    {
        if (cartItem.GiftBundleId.HasValue)
        {
            var bundleItems = GiftBundleSnapshotHelper.Deserialize(cartItem.GiftBundleItemsJson);
            return new CartItemVM
            {
                CartItemId = cartItem.Id,
                GiftBundleId = cartItem.GiftBundleId,
                GiftBundleTitle = cartItem.GiftBundleTitle,
                GiftBundleOriginalTotal = cartItem.GiftBundleOriginalTotal,
                ProductName = cartItem.GiftBundleTitle ?? "Gift Bundle",
                ImageUrl = bundleItems.FirstOrDefault()?.ImageUrl,
                UnitPrice = cartItem.PriceSnapshot,
                Quantity = cartItem.Quantity,
                MaxStock = ResolveBundleMaxStock(bundleItems, bundleVariantStockLookup),
                BundleItems = bundleItems.Select(bundleItem => new GiftBundleCartProductVM
                {
                    ProductName = bundleItem.ProductName,
                    Size = bundleItem.Size,
                    Color = bundleItem.Color,
                    ImageUrl = bundleItem.ImageUrl
                }).ToList()
            };
        }

        if (cartItem.ProductVariant == null)
        {
            return new CartItemVM
            {
                CartItemId = cartItem.Id,
                ProductName = "Unavailable item",
                UnitPrice = cartItem.PriceSnapshot,
                Quantity = cartItem.Quantity,
                MaxStock = 0
            };
        }

        return new CartItemVM
        {
            CartItemId = cartItem.Id,
            ProductVariantId = cartItem.ProductVariantId,
            ProductName = cartItem.ProductVariant.Product.Name,
            Size = cartItem.ProductVariant.Size,
            Color = cartItem.ProductVariant.Color,
            ImageUrl = cartItem.ProductVariant.Product.Images.FirstOrDefault(img => img.IsMain)?.ImageUrl
                       ?? cartItem.ProductVariant.Product.Images.FirstOrDefault()?.ImageUrl,
            UnitPrice = cartItem.PriceSnapshot,
            Quantity = cartItem.Quantity,
            MaxStock = cartItem.ProductVariant.Stock
        };
    }

    private async Task<IReadOnlyDictionary<int, int>> BuildBundleVariantStockLookupAsync(IEnumerable<CartItem> cartItems)
    {
        var variantIds = cartItems
            .Where(item => item.GiftBundleId.HasValue)
            .SelectMany(item => GiftBundleSnapshotHelper.Deserialize(item.GiftBundleItemsJson))
            .Select(item => item.ProductVariantId)
            .Distinct()
            .ToList();

        if (variantIds.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        return (await _uow.ProductVariants.FindAllAsync(v => variantIds.Contains(v.Id), tracked: false, ignoreQueryFilters: true))
            .ToDictionary(variant => variant.Id, variant => variant.Stock);
    }

    private async Task<int> GetGiftBundleMaxStockAsync(IEnumerable<GiftBundleSnapshotItem> bundleItems)
    {
        var variantIds = bundleItems
            .Select(item => item.ProductVariantId)
            .Distinct()
            .ToList();

        if (variantIds.Count == 0)
        {
            return 0;
        }

        var variantLookup = (await _uow.ProductVariants
            .FindAllAsync(v => variantIds.Contains(v.Id), tracked: false, ignoreQueryFilters: true))
            .ToDictionary(variant => variant.Id, variant => variant.Stock);

        return ResolveBundleMaxStock(bundleItems, variantLookup);
    }

    private async Task<(GiftBundle? Bundle, List<GiftBundleSnapshotItem> SnapshotItems, int MaxStock, string? ErrorMessage)> PrepareGiftBundleAsync(int giftBundleId)
    {
        var bundle = await _uow.GiftBundles.FindAsync(
            gb => gb.Id == giftBundleId && gb.IsActive,
            "Items.Product.Images,Items.Product.Variants");

        if (bundle == null)
        {
            return (null, [], 0, "This gift bundle could not be found.");
        }

        var snapshotItems = new List<GiftBundleSnapshotItem>();
        var maxStock = int.MaxValue;

        foreach (var bundleProduct in bundle.Items.OrderBy(item => item.SortOrder))
        {
            var selectedVariant = bundleProduct.Product.Variants
                .Where(variant => variant.IsActive && variant.Stock > 0)
                .OrderBy(variant => variant.Price)
                .FirstOrDefault();

            if (selectedVariant == null)
            {
                return (null, [], 0, $"'{bundleProduct.Product.Name}' is currently unavailable for this bundle.");
            }

            maxStock = Math.Min(maxStock, selectedVariant.Stock);

            snapshotItems.Add(new GiftBundleSnapshotItem
            {
                ProductId = bundleProduct.ProductId,
                ProductVariantId = selectedVariant.Id,
                ProductName = bundleProduct.Product.Name,
                Size = selectedVariant.Size,
                Color = selectedVariant.Color,
                UnitPrice = selectedVariant.Price,
                ImageUrl = bundleProduct.Product.Images.FirstOrDefault(image => image.IsMain)?.ImageUrl
                    ?? bundleProduct.Product.Images.OrderBy(image => image.DisplayOrder).FirstOrDefault()?.ImageUrl
            });
        }

        if (snapshotItems.Count < 2)
        {
            return (null, [], 0, "This gift bundle needs at least 2 valid products.");
        }

        return (bundle, snapshotItems, maxStock == int.MaxValue ? 0 : maxStock, null);
    }

    private static int ResolveBundleMaxStock(IEnumerable<GiftBundleSnapshotItem> bundleItems, IReadOnlyDictionary<int, int> variantStockLookup)
    {
        var stocks = bundleItems
            .Select(item => variantStockLookup.TryGetValue(item.ProductVariantId, out var stock) ? stock : 0)
            .ToList();

        return stocks.Count == 0 ? 0 : stocks.Min();
    }
}
