using System.Security.Claims;
using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Customer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe.Checkout;

namespace ECommerce_System.Areas.Customer.Controllers;

[Area("Customer")]
[Authorize(Roles = SD.Role_AdminOrCustomer)]
public class OrdersController : Controller
{
    private readonly IUnitOfWork                    _unitOfWork;
    private readonly ILogger<OrdersController>      _logger;
    private readonly StripeSettings                 _stripeSettings;
    private const    int                            PageSize = 10;
    private const    string                         PaymentMethodCashOnDelivery = "CashOnDelivery";
    private const    string                         PaymentMethodCreditCard     = "CreditCard";

    public OrdersController(IUnitOfWork unitOfWork, ILogger<OrdersController> logger, IOptions<StripeSettings> stripeOptions)
    {
        _unitOfWork = unitOfWork;
        _logger     = logger;
        _stripeSettings = stripeOptions.Value;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  GET  /Customer/Orders/Checkout
    // ──────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Checkout(string? couponCode = null, int? addressId = null, string? paymentMethod = null)
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
                CurrentPrice     = ci.PriceSnapshot,
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
            Subtotal         = cart.Items.Sum(ci => ci.Quantity * ci.PriceSnapshot),
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
            DefaultAddressId = addresses.FirstOrDefault(a => a.IsDefault)?.Id,
            AddressId        = addressId,
            CouponCode       = couponCode?.Trim(),
            PaymentMethod    = NormalizePaymentMethod(paymentMethod)
        };

        var (_, discountAmount, couponError, appliedCode) = await ResolveCouponAsync(userId, vm.CouponCode, vm.Subtotal);
        vm.DiscountAmount = discountAmount;
        vm.CouponCode = appliedCode ?? vm.CouponCode;
        vm.CouponApplied = string.IsNullOrWhiteSpace(couponError) && discountAmount > 0 && !string.IsNullOrWhiteSpace(vm.CouponCode);
        vm.CouponMessage = couponError ?? (vm.CouponApplied ? $"Coupon '{vm.CouponCode}' applied successfully." : null);

        // Checkout.cshtml lives under Cart/, not Orders/ — specify path explicitly
        return View("~/Areas/Customer/Views/Cart/Checkout.cshtml", vm);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  POST /Customer/Orders/PlaceOrder
    // ──────────────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceOrder(int addressId, string? couponCode, string? paymentMethod)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();
        paymentMethod = NormalizePaymentMethod(paymentMethod);

        _logger.LogInformation("PlaceOrder started. UserId={UserId}, AddressId={AddressId}, Coupon={Coupon}, PaymentMethod={PaymentMethod}",
            userId, addressId, couponCode ?? "none", paymentMethod);

        // ── Step 1: Validate Address ─────────────────────────────────────────
        if (addressId <= 0)
        {
            TempData["Error"] = "Please select a valid delivery address.";
            return RedirectToAction(nameof(Checkout), new { couponCode, addressId, paymentMethod });
        }

        var address = await _unitOfWork.Addresses.FindAsync(
            a => a.Id == addressId && a.UserId == userId);

        if (address == null)
        {
            _logger.LogWarning("PlaceOrder blocked: Address {AddressId} not found for user {UserId}.",
                addressId, userId);
            TempData["Error"] = "The selected address is invalid or does not belong to your account.";
            return RedirectToAction(nameof(Checkout), new { couponCode, addressId, paymentMethod });
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

            subtotal += item.Quantity * item.PriceSnapshot;
        }

        // ── Step 5: Validate & Apply Coupon ──────────────────────────────────
        var (appliedCoupon, discountAmount, couponError, appliedCode) = await ResolveCouponAsync(userId, couponCode, subtotal);
        couponCode = appliedCode ?? couponCode?.Trim();
        if (!string.IsNullOrWhiteSpace(couponError))
        {
            _logger.LogWarning("PlaceOrder: Coupon validation failed. Coupon={Coupon}, UserId={UserId}, Reason={Reason}",
                couponCode ?? "none", userId, couponError);
            TempData["Error"] = couponError;
            return RedirectToAction(nameof(Checkout), new { couponCode, addressId, paymentMethod });
        }

        if (appliedCoupon != null)
        {
            _logger.LogInformation("PlaceOrder: Coupon '{Coupon}' applied. Discount={Discount:C}. UserId={UserId}.",
                appliedCoupon.CouponCode, discountAmount, userId);
        }

        var totalAmount = subtotal - discountAmount;

        if (paymentMethod == PaymentMethodCreditCard)
        {
            if (!IsStripeConfigured())
            {
                TempData["Error"] = "Stripe payment is not configured yet. Add the Stripe Secret Key first, then try again.";
                return RedirectToAction(nameof(Checkout), new { couponCode, addressId, paymentMethod });
            }

            try
            {
                var session = await CreateStripeCheckoutSessionAsync(cart, userId, addressId, couponCode, subtotal, discountAmount, totalAmount);
                if (string.IsNullOrWhiteSpace(session.Url))
                {
                    TempData["Error"] = "Could not start the Stripe checkout session. Please try again.";
                    return RedirectToAction(nameof(Checkout), new { couponCode, addressId, paymentMethod });
                }

                return Redirect(session.Url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PlaceOrder failed while creating Stripe session. UserId={UserId}.", userId);
                TempData["Error"] = "Unable to start card payment right now. Please try again.";
                return RedirectToAction(nameof(Checkout), new { couponCode, addressId, paymentMethod });
            }
        }

        try
        {
            var order = await FinalizeOrderAsync(
                userId,
                addressId,
                cart,
                subtotal,
                discountAmount,
                appliedCoupon,
                SD.Payment_Unpaid,
                paymentProvider: null,
                transactionId: null,
                couponCodeOverride: couponCode);

            _logger.LogInformation(
                "PlaceOrder succeeded. OrderId={OrderId}, Total={Total:C}, UserId={UserId}.",
                order.Id, totalAmount, userId);

            return RedirectToAction(nameof(Success), new { id = order.Id });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex,
                "PlaceOrder concurrency conflict for UserId={UserId}. Stock was modified by another request.", userId);
            TempData["Error"] = "One or more products were updated while your order was being placed. " +
                                "Please review your cart and try again.";
            return RedirectToAction("Index", "Cart");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlaceOrder failed for UserId={UserId}.", userId);
            TempData["Error"] = "Something went wrong while placing your order. Please try again.";
            return RedirectToAction(nameof(Checkout), new { couponCode, addressId, paymentMethod });
        }
    }

    [HttpGet]
    public async Task<IActionResult> StripeSuccess(string sessionId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            TempData["Error"] = "Missing Stripe session details.";
            return RedirectToAction(nameof(Checkout), new { paymentMethod = PaymentMethodCreditCard });
        }

        if (!IsStripeConfigured())
        {
            TempData["Error"] = "Stripe payment is not configured yet. Add the Stripe Secret Key first, then try again.";
            return RedirectToAction(nameof(Checkout), new { paymentMethod = PaymentMethodCreditCard });
        }

        try
        {
            var sessionService = new SessionService();
            var session = await sessionService.GetAsync(sessionId);

            if (session == null || !string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Stripe payment was not completed successfully.";
                return RedirectToAction(nameof(Checkout), new { paymentMethod = PaymentMethodCreditCard });
            }

            var metadata = session.Metadata ?? new Dictionary<string, string>();
            var sessionUserId = metadata.TryGetValue("UserId", out var metadataUserId) ? metadataUserId : session.ClientReferenceId;
            if (!string.Equals(sessionUserId, userId, StringComparison.Ordinal))
            {
                _logger.LogWarning("StripeSuccess blocked due to user mismatch. SessionId={SessionId}, SessionUserId={SessionUserId}, CurrentUserId={UserId}.",
                    sessionId, sessionUserId, userId);
                return Forbid();
            }

            var existingPayment = await _unitOfWork.Payments
                .Query()
                .Include(p => p.Order)
                .FirstOrDefaultAsync(p => p.TransactionId == sessionId);

            if (existingPayment?.Order != null)
            {
                return RedirectToAction(nameof(Success), new { id = existingPayment.Order.Id });
            }

            if (!metadata.TryGetValue("AddressId", out var addressValue) || !int.TryParse(addressValue, out var addressId))
            {
                TempData["Error"] = "Stripe session is missing address information.";
                return RedirectToAction(nameof(Checkout), new { paymentMethod = PaymentMethodCreditCard });
            }

            metadata.TryGetValue("CouponCode", out var couponCode);

            var address = await _unitOfWork.Addresses.FindAsync(a => a.Id == addressId && a.UserId == userId);
            if (address == null)
            {
                TempData["Error"] = "The selected delivery address is no longer available.";
                return RedirectToAction(nameof(Checkout), new { couponCode, paymentMethod = PaymentMethodCreditCard });
            }

            var cart = await _unitOfWork.Carts.GetCartByUserIdAsync(userId);
            if (cart == null || !cart.Items.Any())
            {
                TempData["Error"] = "Your cart is empty. We could not finalize the paid order.";
                return RedirectToAction("Index", "Cart");
            }

            decimal subtotal = 0;
            foreach (var item in cart.Items)
            {
                if (item.ProductVariant == null || !item.ProductVariant.IsActive)
                {
                    TempData["Error"] = "One or more products in your cart are no longer available.";
                    return RedirectToAction("Index", "Cart");
                }

                if (item.Quantity > item.ProductVariant.Stock)
                {
                    TempData["Error"] = $"Not enough stock for '{item.ProductVariant.Product?.Name}'. Please review your cart.";
                    return RedirectToAction("Index", "Cart");
                }

                subtotal += item.Quantity * item.PriceSnapshot;
            }

            decimal discountAmount = 0m;
            if (metadata.TryGetValue("DiscountAmount", out var discountValue))
            {
                decimal.TryParse(discountValue, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out discountAmount);
            }
            discountAmount = Math.Min(discountAmount, subtotal);

            Discount? appliedCoupon = null;
            if (!string.IsNullOrWhiteSpace(couponCode))
            {
                var normalizedCode = couponCode.Trim().ToUpperInvariant();
                appliedCoupon = await _unitOfWork.Discounts.FindAsync(
                    d => d.CouponCode.ToUpper() == normalizedCode,
                    ignoreQueryFilters: true);
                couponCode = normalizedCode;
            }

            var order = await FinalizeOrderAsync(
                userId,
                addressId,
                cart,
                subtotal,
                discountAmount,
                appliedCoupon,
                SD.Payment_Paid,
                paymentProvider: "Stripe",
                transactionId: sessionId,
                couponCodeOverride: couponCode);

            return RedirectToAction(nameof(Success), new { id = order.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StripeSuccess failed. SessionId={SessionId}, UserId={UserId}.", sessionId, userId);
            TempData["Error"] = "We couldn't finalize the Stripe payment right now. Please contact support if you were charged.";
            return RedirectToAction(nameof(Checkout), new { paymentMethod = PaymentMethodCreditCard });
        }
    }

    [HttpGet]
    public IActionResult StripeCancel(string? couponCode = null, int? addressId = null)
    {
        TempData["Error"] = "Card payment was canceled before completion.";
        return RedirectToAction(nameof(Checkout), new { couponCode, addressId, paymentMethod = PaymentMethodCreditCard });
    }

    [HttpGet]
    public async Task<IActionResult> Success(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        if (id <= 0) return NotFound();

        var order = await _unitOfWork.Orders.GetOrderWithDetailsAsync(id);
        if (order == null) return NotFound();

        if (order.UserId != userId)
        {
            _logger.LogWarning("Success: UserId={UserId} attempted to view OrderId={OrderId} owned by {Owner}.",
                userId, id, order.UserId);
            return Forbid();
        }

        var estimatedFrom = order.Shipment?.EstimatedDelivery ?? DateOnly.FromDateTime(order.CreatedAt.AddDays(3));
        var estimatedTo = order.Shipment?.EstimatedDelivery ?? DateOnly.FromDateTime(order.CreatedAt.AddDays(5));

        var vm = new OrderSuccessCustomerVM
        {
            Id = order.Id,
            CustomerEmail = order.User?.Email ?? string.Empty,
            CreatedAt = order.CreatedAt,
            EstimatedDeliveryFrom = estimatedFrom,
            EstimatedDeliveryTo = estimatedTo,
            ShippingLabel = string.IsNullOrWhiteSpace(order.Shipment?.Carrier) ? "Standard Shipping" : order.Shipment.Carrier!,
            PaymentStatus = order.PaymentStatus
        };

        return View(vm);
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

    private static string NormalizePaymentMethod(string? paymentMethod)
    {
        return paymentMethod == PaymentMethodCreditCard
            ? PaymentMethodCreditCard
            : PaymentMethodCashOnDelivery;
    }

    private bool IsStripeConfigured()
    {
        return !string.IsNullOrWhiteSpace(_stripeSettings.SecretKey);
    }

    private async Task<Session> CreateStripeCheckoutSessionAsync(
        Cart cart,
        string userId,
        int addressId,
        string? couponCode,
        decimal subtotal,
        decimal discountAmount,
        decimal totalAmount)
    {
        var sessionService = new SessionService();
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var successUrl = $"{baseUrl}{Url.Action(nameof(StripeSuccess), "Orders", new { area = "Customer" })}?sessionId={{CHECKOUT_SESSION_ID}}";
        var cancelUrl = $"{baseUrl}{Url.Action(nameof(StripeCancel), "Orders", new { area = "Customer", couponCode, addressId })}";

        var lineItems = new List<SessionLineItemOptions>();

        if (discountAmount > 0)
        {
            lineItems.Add(new SessionLineItemOptions
            {
                Quantity = 1,
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = "egp",
                    UnitAmount = (long)Math.Round(totalAmount * 100m, MidpointRounding.AwayFromZero),
                    ProductData = new SessionLineItemPriceDataProductDataOptions
                    {
                        Name = "Smart Store Order",
                        Description = string.IsNullOrWhiteSpace(couponCode)
                            ? "Final order total"
                            : $"Final order total after coupon {couponCode}"
                    }
                }
            });
        }
        else
        {
            foreach (var item in cart.Items)
            {
                lineItems.Add(new SessionLineItemOptions
                {
                    Quantity = item.Quantity,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "egp",
                        UnitAmount = (long)Math.Round(item.PriceSnapshot * 100m, MidpointRounding.AwayFromZero),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = item.ProductVariant.Product?.Name ?? "Smart Store Product",
                            Description = $"{item.ProductVariant.Size} / {item.ProductVariant.Color}"
                        }
                    }
                });
            }
        }

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            ClientReferenceId = userId,
            CustomerEmail = User.FindFirstValue(ClaimTypes.Email),
            LineItems = lineItems,
            Metadata = new Dictionary<string, string>
            {
                ["UserId"] = userId,
                ["AddressId"] = addressId.ToString(),
                ["CouponCode"] = couponCode ?? string.Empty,
                ["Subtotal"] = subtotal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["DiscountAmount"] = discountAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["TotalAmount"] = totalAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        };

        return await sessionService.CreateAsync(options);
    }

    private async Task<Order> FinalizeOrderAsync(
        string userId,
        int addressId,
        Cart cart,
        decimal subtotal,
        decimal discountAmount,
        Discount? appliedCoupon,
        string paymentStatus,
        string? paymentProvider,
        string? transactionId,
        string? couponCodeOverride = null)
    {
        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var orderItems = new List<OrderItem>(cart.Items.Count);

            foreach (var item in cart.Items)
            {
                item.ProductVariant.Stock -= item.Quantity;
                if (item.ProductVariant.Stock == 0)
                {
                    item.ProductVariant.IsActive = false;
                    _logger.LogInformation(
                        "FinalizeOrder: Variant {VariantId} will be deactivated (stock → 0). UserId={UserId}.",
                        item.ProductVariantId, userId);
                }
                _unitOfWork.ProductVariants.Update(item.ProductVariant);

                orderItems.Add(new OrderItem
                {
                    ProductVariantId = item.ProductVariantId,
                    ProductName      = item.ProductVariant.Product?.Name ?? "(deleted)",
                    Size             = item.ProductVariant.Size,
                    Color            = item.ProductVariant.Color,
                    Quantity         = item.Quantity,
                    UnitPrice        = item.PriceSnapshot,
                    Subtotal         = item.Quantity * item.PriceSnapshot
                });
            }

            var totalAmount = subtotal - discountAmount;
            var order = new Order
            {
                UserId         = userId,
                AddressId      = addressId,
                Subtotal       = subtotal,
                DiscountAmount = discountAmount,
                TotalAmount    = totalAmount,
                CouponCode     = appliedCoupon?.CouponCode ?? couponCodeOverride,
                Status         = SD.Status_Pending,
                PaymentStatus  = paymentStatus,
                OrderItems     = orderItems,
                CreatedAt      = DateTime.UtcNow,
                UpdatedAt      = DateTime.UtcNow
            };

            if (!string.IsNullOrWhiteSpace(paymentProvider) || !string.IsNullOrWhiteSpace(transactionId))
            {
                order.Payment = new Payment
                {
                    Amount = totalAmount,
                    Provider = string.IsNullOrWhiteSpace(paymentProvider) ? "Stripe" : paymentProvider,
                    TransactionId = transactionId,
                    Status = paymentStatus == SD.Payment_Paid ? SD.Payment_Paid : SD.Payment_Pending
                };
            }

            await _unitOfWork.Orders.AddAsync(order);

            if (appliedCoupon != null)
            {
                appliedCoupon.UsageCount++;
                _unitOfWork.Discounts.Update(appliedCoupon);
            }

            _unitOfWork.CartItems.RemoveRange(cart.Items);

            await _unitOfWork.SaveAsync();
            await transaction.CommitAsync();
            return order;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<(Discount? Coupon, decimal DiscountAmount, string? ErrorMessage, string? AppliedCode)> ResolveCouponAsync(string userId, string? couponCode, decimal subtotal)
    {
        if (string.IsNullOrWhiteSpace(couponCode))
        {
            return (null, 0m, null, null);
        }

        var normalizedCode = couponCode.Trim().ToUpperInvariant();
        var appliedCoupon = await _unitOfWork.Discounts.FindAsync(
            d => d.CouponCode.ToUpper() == normalizedCode && d.IsActive);

        if (appliedCoupon == null)
        {
            return (null, 0m, $"Coupon code '{couponCode}' is invalid or has been deactivated.", normalizedCode);
        }

        if (appliedCoupon.ExpiresAt.HasValue && appliedCoupon.ExpiresAt.Value < DateTime.UtcNow)
        {
            return (null, 0m, $"Coupon code '{appliedCoupon.CouponCode}' has expired.", appliedCoupon.CouponCode);
        }

        if (appliedCoupon.UsageLimit.HasValue && appliedCoupon.UsageCount >= appliedCoupon.UsageLimit.Value)
        {
            return (null, 0m, $"Coupon code '{appliedCoupon.CouponCode}' has reached its usage limit.", appliedCoupon.CouponCode);
        }

        var alreadyUsedByCustomer = await _unitOfWork.Orders
            .Query()
            .AnyAsync(o => o.UserId == userId && o.CouponCode == appliedCoupon.CouponCode);

        if (alreadyUsedByCustomer)
        {
            return (null, 0m, $"You have already used coupon code '{appliedCoupon.CouponCode}'.", appliedCoupon.CouponCode);
        }

        if (appliedCoupon.MinimumOrderAmount.HasValue && subtotal < appliedCoupon.MinimumOrderAmount.Value)
        {
            return (null, 0m,
                $"This coupon requires a minimum order of {appliedCoupon.MinimumOrderAmount.Value:C}. Your subtotal is {subtotal:C}.",
                appliedCoupon.CouponCode);
        }

        var discountAmount = appliedCoupon.Type == SD.Discount_Percentage
            ? subtotal * (appliedCoupon.Value / 100m)
            : appliedCoupon.Value;

        discountAmount = Math.Min(discountAmount, subtotal);

        return (appliedCoupon, discountAmount, null, appliedCoupon.CouponCode);
    }
}
