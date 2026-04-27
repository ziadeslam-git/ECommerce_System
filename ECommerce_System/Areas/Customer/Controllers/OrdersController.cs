using System.Security.Claims;
using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Customer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECommerce_System.Areas.Customer.Controllers;

[Area("Customer")]
[Authorize(Roles = SD.Role_Customer)]
public class OrdersController : Controller
{
    private readonly IUnitOfWork                    _unitOfWork;
    private readonly ILogger<OrdersController>      _logger;
    private const    int                            PageSize = 10;

    public OrdersController(IUnitOfWork unitOfWork, ILogger<OrdersController> logger)
    {
        _unitOfWork = unitOfWork;
        _logger     = logger;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  GET  /Customer/Orders/Checkout
    // ──────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Checkout()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var cart = await _unitOfWork.Carts.GetCartByUserIdAsync(userId);
        if (cart == null || !cart.Items.Any())
        {
            TempData["Info"] = "Your cart is empty. Add some products before checking out.";
            return RedirectToAction("Index", "Cart");
        }

        var addresses = await _unitOfWork.Addresses.FindAllAsync(a => a.UserId == userId);

        var vm = new CheckoutVM
        {
            Items = cart.Items.Select(ci => new CheckoutItemCustomerVM
            {
                CartItemId       = ci.Id,
                ProductVariantId = ci.ProductVariantId,
                Quantity         = ci.Quantity,
                PriceSnapshot    = ci.PriceSnapshot,
                CurrentPrice     = ci.ProductVariant.Price,
                ProductName      = ci.ProductVariant.Product.Name,
                Size             = ci.ProductVariant.Size,
                Color            = ci.ProductVariant.Color,
                ImageUrl         = ci.ProductVariant.Product.Images
                                     .FirstOrDefault(i => i.IsMain)?.ImageUrl
                                   ?? ci.ProductVariant.Product.Images
                                     .OrderBy(i => i.DisplayOrder)
                                     .FirstOrDefault()?.ImageUrl,
                Stock            = ci.ProductVariant.Stock
            }).ToList(),
            Subtotal         = cart.Items.Sum(ci => ci.Quantity * ci.ProductVariant.Price),
            Addresses        = addresses.Select(a => new AddressOptionCustomerVM
            {
                Id          = a.Id,
                FullName    = a.FullName,
                PhoneNumber = a.PhoneNumber,
                Street      = a.Street,
                City        = a.City,
                State       = a.State,
                Country     = a.Country,
                PostalCode  = a.PostalCode,
                IsDefault   = a.IsDefault,
                DisplayLine = BuildAddressLine(a)
            }).ToList(),
            DefaultAddressId = addresses.FirstOrDefault(a => a.IsDefault)?.Id
        };

        // Checkout.cshtml lives under Cart/, not Orders/ — specify path explicitly
        return View("~/Areas/Customer/Views/Cart/Checkout.cshtml", vm);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  POST /Customer/Orders/PlaceOrder
    // ──────────────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceOrder(int addressId, string? couponCode)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        _logger.LogInformation("PlaceOrder started. UserId={UserId}, AddressId={AddressId}, Coupon={Coupon}",
            userId, addressId, couponCode ?? "none");

        // ── Step 1: Validate Address ─────────────────────────────────────────
        if (addressId <= 0)
        {
            TempData["Error"] = "Please select a valid delivery address.";
            return RedirectToAction(nameof(Checkout));
        }

        var address = await _unitOfWork.Addresses.FindAsync(
            a => a.Id == addressId && a.UserId == userId);

        if (address == null)
        {
            _logger.LogWarning("PlaceOrder blocked: Address {AddressId} not found for user {UserId}.",
                addressId, userId);
            TempData["Error"] = "The selected address is invalid or does not belong to your account.";
            return RedirectToAction(nameof(Checkout));
        }

        // ── Step 2: Load Cart ─────────────────────────────────────────────────
        var cart = await _unitOfWork.Carts.GetCartByUserIdAsync(userId);
        if (cart == null || !cart.Items.Any())
        {
            TempData["Info"] = "Your cart is empty.";
            return RedirectToAction("Index", "Cart");
        }

        // ── Steps 3 & 4: Server-side stock check + subtotal ───────────────────
        decimal subtotal = 0;
        foreach (var item in cart.Items)
        {
            // Guard: variant may be null if data integrity is broken
            if (item.ProductVariant == null)
            {
                _logger.LogError("PlaceOrder: CartItem {ItemId} has no ProductVariant. UserId={UserId}.",
                    item.Id, userId);
                TempData["Error"] = "One or more products in your cart are no longer available. Please review your cart.";
                return RedirectToAction("Index", "Cart");
            }

            if (!item.ProductVariant.IsActive)
            {
                TempData["Error"] = $"{item.ProductVariant.Product?.Name} ({item.ProductVariant.Size} / {item.ProductVariant.Color}) is no longer available.";
                return RedirectToAction("Index", "Cart");
            }

            if (item.Quantity > item.ProductVariant.Stock)
            {
                TempData["Error"] = $"Not enough stock for '{item.ProductVariant.Product?.Name}' ({item.ProductVariant.Size} / {item.ProductVariant.Color}). " +
                                    $"Available: {item.ProductVariant.Stock}, Requested: {item.Quantity}.";
                return RedirectToAction("Index", "Cart");
            }

            subtotal += item.Quantity * item.ProductVariant.Price;
        }

        // ── Step 5: Validate & Apply Coupon ──────────────────────────────────
        decimal   discountAmount = 0;
        Discount? appliedCoupon  = null;

        if (!string.IsNullOrWhiteSpace(couponCode))
        {
            var normalizedCode = couponCode.Trim().ToUpperInvariant();
            appliedCoupon = await _unitOfWork.Discounts.FindAsync(
                d => d.CouponCode.ToUpper() == normalizedCode && d.IsActive);

            if (appliedCoupon == null)
            {
                _logger.LogWarning("PlaceOrder: Coupon '{Coupon}' not found or inactive. UserId={UserId}.", couponCode, userId);
                TempData["Error"] = $"Coupon code '{couponCode}' is invalid or has been deactivated.";
                return RedirectToAction(nameof(Checkout));
            }

            if (appliedCoupon.ExpiresAt.HasValue && appliedCoupon.ExpiresAt.Value < DateTime.UtcNow)
            {
                TempData["Error"] = $"Coupon code '{couponCode}' has expired.";
                return RedirectToAction(nameof(Checkout));
            }

            if (appliedCoupon.UsageLimit.HasValue && appliedCoupon.UsageCount >= appliedCoupon.UsageLimit.Value)
            {
                TempData["Error"] = $"Coupon code '{couponCode}' has reached its usage limit.";
                return RedirectToAction(nameof(Checkout));
            }

            if (appliedCoupon.MinimumOrderAmount.HasValue && subtotal < appliedCoupon.MinimumOrderAmount.Value)
            {
                TempData["Error"] = $"This coupon requires a minimum order of {appliedCoupon.MinimumOrderAmount.Value:C}. " +
                                    $"Your subtotal is {subtotal:C}.";
                return RedirectToAction(nameof(Checkout));
            }

            discountAmount = appliedCoupon.Type == SD.Discount_Percentage
                ? subtotal * (appliedCoupon.Value / 100m)
                : appliedCoupon.Value;

            // Cap: discount must not exceed subtotal
            discountAmount = Math.Min(discountAmount, subtotal);

            _logger.LogInformation("PlaceOrder: Coupon '{Coupon}' applied. Discount={Discount:C}. UserId={UserId}.",
                appliedCoupon.CouponCode, discountAmount, userId);
        }

        var totalAmount = subtotal - discountAmount;

        // ── Steps 6-10: Transactional section ────────────────────────────────
        // KEY DESIGN: We build everything in memory first, then ONE SaveAsync + Commit.
        // This avoids double SaveAsync (which held two sets of DB locks) and eliminates
        // the N+1 await-in-loop pattern.
        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            // ── Step 6 & 8: Deduct stock + build OrderItems snapshot in memory ──
            var orderItems = new List<OrderItem>(cart.Items.Count);

            foreach (var item in cart.Items)
            {
                // Stock deduction (in-memory; flushed in single SaveAsync below)
                item.ProductVariant.Stock -= item.Quantity;
                if (item.ProductVariant.Stock == 0)
                {
                    item.ProductVariant.IsActive = false;
                    _logger.LogInformation(
                        "PlaceOrder: Variant {VariantId} will be deactivated (stock → 0). UserId={UserId}.",
                        item.ProductVariantId, userId);
                }
                _unitOfWork.ProductVariants.Update(item.ProductVariant);

                // Snapshot — OrderId FK will be set by EF when Order is saved
                orderItems.Add(new OrderItem
                {
                    ProductVariantId = item.ProductVariantId,
                    ProductName      = item.ProductVariant.Product?.Name ?? "(deleted)",
                    Size             = item.ProductVariant.Size,
                    Color            = item.ProductVariant.Color,
                    Quantity         = item.Quantity,
                    UnitPrice        = item.ProductVariant.Price,
                    Subtotal         = item.Quantity * item.ProductVariant.Price
                });
            }

            // ── Step 7: Create Order and attach items via navigation property ──
            // EF Core resolves the FK (OrderItem.OrderId) automatically on SaveAsync.
            // No intermediate SaveAsync needed → no intermediate DB lock round-trip.
            var order = new Order
            {
                UserId         = userId,
                AddressId      = addressId,
                Subtotal       = subtotal,
                DiscountAmount = discountAmount,
                TotalAmount    = totalAmount,
                CouponCode     = appliedCoupon?.CouponCode,
                Status         = SD.Status_Pending,
                PaymentStatus  = SD.Payment_Unpaid,
                OrderItems     = orderItems,          // ← EF sets FK automatically
                CreatedAt      = DateTime.UtcNow,
                UpdatedAt      = DateTime.UtcNow
            };
            await _unitOfWork.Orders.AddAsync(order); // tracks Order + all OrderItems

            // ── Step 5b: Increment coupon usage ──────────────────────────────
            if (appliedCoupon != null)
            {
                appliedCoupon.UsageCount++;
                _unitOfWork.Discounts.Update(appliedCoupon);
            }

            // ── Step 9: Clear Cart ────────────────────────────────────────────
            _unitOfWork.CartItems.RemoveRange(cart.Items);

            // ── Step 10: Single SaveAsync → One round trip, one set of locks ─
            await _unitOfWork.SaveAsync();
            await transaction.CommitAsync();

            _logger.LogInformation(
                "PlaceOrder succeeded. OrderId={OrderId}, Items={Count}, Total={Total:C}, UserId={UserId}.",
                order.Id, orderItems.Count, totalAmount, userId);

            TempData["Success"] = $"Order #{order.Id} placed successfully! We will keep you updated on the status.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex,
                "PlaceOrder concurrency conflict for UserId={UserId}. Stock was modified by another request.", userId);
            TempData["Error"] = "One or more products were updated while your order was being placed. " +
                                "Please review your cart and try again.";
            return RedirectToAction("Index", "Cart");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "PlaceOrder failed for UserId={UserId}.", userId);
            TempData["Error"] = "Something went wrong while placing your order. Please try again.";
            return RedirectToAction(nameof(Checkout));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  GET  /Customer/Orders/Index?page=1
    // ──────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        page = Math.Max(1, page);

        var (orders, totalCount) = await _unitOfWork.Orders
            .GetOrdersByUserPagedAsync(userId, page, PageSize);

        var vmOrders = orders.Select(o =>
        {
            // Main image = IsMain image of the first OrderItem's product
            var firstItem   = o.OrderItems.FirstOrDefault();
            var mainImgUrl  = firstItem?.ProductVariant?.Product?.Images
                                         .FirstOrDefault(i => i.IsMain)?.ImageUrl
                           ?? firstItem?.ProductVariant?.Product?.Images
                                         .OrderBy(i => i.DisplayOrder)
                                         .FirstOrDefault()?.ImageUrl;
            return new OrderIndexCustomerVM
            {
                Id            = o.Id,
                CreatedAt     = o.CreatedAt,
                Status        = o.Status,
                PaymentStatus = o.PaymentStatus,
                TotalAmount   = o.TotalAmount,
                ItemCount     = o.OrderItems.Sum(oi => oi.Quantity),
                MainImageUrl  = mainImgUrl
            };
        }).ToList();

        var vm = new OrderIndexPagedVM
        {
            Orders      = vmOrders,
            CurrentPage = page,
            TotalPages  = (int)Math.Ceiling(totalCount / (double)PageSize),
            TotalCount  = totalCount
        };

        return View(vm);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  GET  /Customer/Orders/Details/{id}
    // ──────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        if (id <= 0) return NotFound();

        var order = await _unitOfWork.Orders.GetOrderWithDetailsAsync(id);

        if (order == null) return NotFound();

        // Security: only the owner can see their order
        if (order.UserId != userId)
        {
            _logger.LogWarning("Details: UserId={UserId} attempted to view OrderId={OrderId} owned by {Owner}.",
                userId, id, order.UserId);
            return Forbid();
        }

        var vm = new OrderDetailsCustomerVM
        {
            Id             = order.Id,
            CreatedAt      = order.CreatedAt,
            Status         = order.Status,
            PaymentStatus  = order.PaymentStatus,
            Subtotal       = order.Subtotal,
            DiscountAmount = order.DiscountAmount,
            TotalAmount    = order.TotalAmount,
            CouponCode     = order.CouponCode,
            AddressLine    = BuildAddressLine(order.Address),
            Shipment       = order.Shipment != null ? new ShipmentSummaryCustomerVM
            {
                TrackingNumber    = order.Shipment.TrackingNumber,
                Carrier           = order.Shipment.Carrier,
                Status            = order.Shipment.Status,
                EstimatedDelivery = order.Shipment.EstimatedDelivery,
                ShippedAt         = order.Shipment.ShippedAt,
                DeliveredAt       = order.Shipment.DeliveredAt
            } : null,
            Items = order.OrderItems.Select(oi => new OrderItemCustomerVM
            {
                ProductName = oi.ProductName,
                Size        = oi.Size,
                Color       = oi.Color,
                Quantity    = oi.Quantity,
                UnitPrice   = oi.UnitPrice,
                Subtotal    = oi.Subtotal
            }).ToList()
        };

        return View(vm);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  POST /Customer/Orders/Cancel/{id}
    // ──────────────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        if (id <= 0) return NotFound();

        var order = await _unitOfWork.Orders.FindAsync(
            o => o.Id == id,
            includeProperties: "OrderItems,OrderItems.ProductVariant");

        if (order == null) return NotFound();

        // Security: only the owner can cancel
        if (order.UserId != userId)
        {
            _logger.LogWarning("Cancel: UserId={UserId} attempted to cancel OrderId={OrderId} owned by {Owner}.",
                userId, id, order.UserId);
            return Forbid();
        }

        // Business rule: only Pending orders can be cancelled
        if (order.Status != SD.Status_Pending)
        {
            TempData["Error"] = $"Order #{id} cannot be cancelled because its status is '{order.Status}'. " +
                                 "Only Pending orders can be cancelled.";
            return RedirectToAction(nameof(Details), new { id });
        }

        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            // Restore stock for each item
            foreach (var item in order.OrderItems)
            {
                if (item.ProductVariant == null)
                {
                    _logger.LogWarning("Cancel: OrderItem {ItemId} in Order {OrderId} has no ProductVariant. Skipping stock restore.",
                        item.Id, id);
                    continue;
                }

                item.ProductVariant.Stock += item.Quantity;

                // Re-activate variant if it had been deactivated due to zero stock
                if (!item.ProductVariant.IsActive && item.ProductVariant.Stock > 0)
                {
                    item.ProductVariant.IsActive = true;
                    _logger.LogInformation("Cancel: Variant {VariantId} re-activated after stock restore. OrderId={OrderId}.",
                        item.ProductVariant.Id, id);
                }

                _unitOfWork.ProductVariants.Update(item.ProductVariant);
            }

            order.Status      = SD.Status_Cancelled;
            order.CancelledAt = DateTime.UtcNow;
            order.UpdatedAt   = DateTime.UtcNow;
            _unitOfWork.Orders.Update(order);

            await _unitOfWork.SaveAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Cancel succeeded. OrderId={OrderId}, UserId={UserId}.", id, userId);
            TempData["Success"] = $"Order #{id} has been cancelled successfully.";
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex, "Cancel concurrency conflict. OrderId={OrderId}, UserId={UserId}.", id, userId);
            TempData["Error"] = "A conflict occurred while cancelling the order. Please try again.";
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Cancel failed. OrderId={OrderId}, UserId={UserId}.", id, userId);
            TempData["Error"] = "Something went wrong while cancelling the order. Please try again.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────────────────────────────────
    private static string BuildAddressLine(Address? addr)
    {
        if (addr == null) return "Address not available";

        var parts = new List<string> { addr.Street, addr.City };
        if (!string.IsNullOrWhiteSpace(addr.State))   parts.Add(addr.State);
        parts.Add(addr.PostalCode);
        parts.Add(addr.Country);
        return string.Join(", ", parts);
    }
}
