using System.Security.Claims;
using System.Text.Json;
using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Resources;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Customer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Stripe.Checkout;

namespace ECommerce_System.Areas.Customer.Controllers;

[Area("Customer")]
[Authorize(Roles = SD.Role_AdminOrCustomer)]
public class OrdersController : Controller
{
    private readonly IUnitOfWork                    _unitOfWork;
    private readonly UserManager<ApplicationUser>   _userManager;
    private readonly IEmailSender                   _emailSender;
    private readonly ILogger<OrdersController>      _logger;
    private readonly StripeSettings                 _stripeSettings;
    private readonly IConfiguration                 _configuration;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private const    int                            PageSize = 10;
    private const    string                         PaymentMethodCashOnDelivery = "CashOnDelivery";
    private const    string                         PaymentMethodCreditCard     = "CreditCard";
    private const    string                         CheckoutViewPath            = "~/Areas/Customer/Views/Cart/Checkout.cshtml";
    private const    string                         CheckoutStateTempDataKey    = "checkout_state";

    public OrdersController(
        IUnitOfWork unitOfWork,
        UserManager<ApplicationUser> userManager,
        IEmailSender emailSender,
        ILogger<OrdersController> logger,
        IConfiguration configuration,
        IOptions<StripeSettings> stripeOptions,
        IStringLocalizer<SharedResource> localizer)
    {
        _unitOfWork = unitOfWork;
        _userManager = userManager;
        _emailSender = emailSender;
        _logger     = logger;
        _configuration = configuration;
        _stripeSettings = stripeOptions.Value;
        _localizer = localizer;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  GET  /Customer/Orders/Checkout
    // ──────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Checkout([FromQuery] CheckoutVM? request)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var hasExplicitRequest = request is not null &&
                                     (request.AddressId.HasValue ||
                                      !string.IsNullOrWhiteSpace(request.CouponCode) ||
                                      request.ShowNewAddressForm ||
                                      request.HasNewAddressInput ||
                                      request.PaymentMethod == PaymentMethodCreditCard);

            if (!hasExplicitRequest)
            {
                request = ReadCheckoutState();
            }

            var cart = await _unitOfWork.Carts.GetCartByUserIdAsync(userId);
            if (cart == null || !cart.Items.Any())
            {
                TempData["error"] = _localizer["CartEmptyBeforeCheckout"].Value;
                return RedirectToAction("Index", "Cart");
            }

            var vm = await BuildCheckoutViewModelAsync(userId, request);
            return View(CheckoutViewPath, vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Checkout GET failed.");
            TempData["error"] = _localizer["SomethingWentWrongWhileLoadingCheckout"].Value;
            return RedirectToAction("Error", "Home", new { area = "Customer" });
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  POST /Customer/Orders/UseNewAddress
    // ──────────────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UseNewAddress(CheckoutVM vm)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            vm.PaymentMethod = NormalizePaymentMethod(vm.PaymentMethod);
            vm.CouponCode = vm.CouponCode?.Trim();
            vm.ShowNewAddressForm = true;

            if (!TryValidateInlineAddress(vm))
            {
                StoreCheckoutState(vm);
                TempData["error"] = _localizer["PleaseCompleteNewAddressBeforeSaving"].Value;
                return RedirectToAction(nameof(Checkout));
            }

            var existingAddresses = await _unitOfWork.Addresses.FindAllAsync(a => a.UserId == userId);
            var makeDefault = !existingAddresses.Any() || vm.SaveNewAddress;
            var address = await CreateInlineAddressAsync(userId, vm, makeDefault);

            TempData["success"] = _localizer["AddressSavedSuccessfully"].Value;
            return RedirectToAction(nameof(Checkout), new
            {
                addressId = address.Id,
                couponCode = vm.CouponCode,
                paymentMethod = vm.PaymentMethod,
                showNewAddressForm = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UseNewAddress failed.");
            TempData["error"] = _localizer["SomethingWentWrongWhileSavingNewAddress"].Value;
            return RedirectToAction("Error", "Home", new { area = "Customer" });
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  POST /Customer/Orders/SelectSavedAddress
    // ──────────────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SelectSavedAddress(CheckoutVM vm)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            vm.PaymentMethod = NormalizePaymentMethod(vm.PaymentMethod);
            vm.CouponCode = vm.CouponCode?.Trim();

            if (!vm.AddressId.HasValue || vm.AddressId.Value <= 0)
            {
                return RedirectToAction(nameof(Checkout), new
                {
                    couponCode = vm.CouponCode,
                    paymentMethod = vm.PaymentMethod,
                    showNewAddressForm = false
                });
            }

            var selectedAddress = await _unitOfWork.Addresses.FindAsync(
                a => a.Id == vm.AddressId.Value && a.UserId == userId);

            if (selectedAddress == null)
            {
                TempData["error"] = _localizer["SelectedAddressNotFound"].Value;
                return RedirectToAction(nameof(Checkout), new
                {
                    couponCode = vm.CouponCode,
                    paymentMethod = vm.PaymentMethod,
                    showNewAddressForm = false
                });
            }

            var userAddresses = await _unitOfWork.Addresses.FindAllAsync(a => a.UserId == userId);
            var changed = false;

            foreach (var address in userAddresses)
            {
                var shouldBeDefault = address.Id == selectedAddress.Id;
                if (address.IsDefault != shouldBeDefault)
                {
                    address.IsDefault = shouldBeDefault;
                    _unitOfWork.Addresses.Update(address);
                    changed = true;
                }
            }

            if (changed)
            {
                await _unitOfWork.SaveAsync();
            }

            return RedirectToAction(nameof(Checkout), new
            {
                addressId = selectedAddress.Id,
                couponCode = vm.CouponCode,
                paymentMethod = vm.PaymentMethod,
                showNewAddressForm = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SelectSavedAddress failed.");
            TempData["error"] = _localizer["SomethingWentWrongWhileSelectingAddress"].Value;
            return RedirectToAction("Error", "Home", new { area = "Customer" });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAddress(int deleteAddressId, CheckoutVM vm)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            vm.PaymentMethod = NormalizePaymentMethod(vm.PaymentMethod);
            vm.CouponCode = vm.CouponCode?.Trim();

            var address = await _unitOfWork.Addresses.FindAsync(a => a.Id == deleteAddressId && a.UserId == userId);
            if (address == null)
            {
                TempData["error"] = _localizer["SelectedAddressNotFound"].Value;
                return RedirectToAction(nameof(Checkout), new { couponCode = vm.CouponCode, paymentMethod = vm.PaymentMethod });
            }

            var hasRelatedOrders = await _unitOfWork.Orders.Query().AnyAsync(o => o.AddressId == deleteAddressId);
            if (hasRelatedOrders)
            {
                TempData["error"] = _localizer["AddressCannotBeDeletedUsedInPreviousOrders"].Value;
                return RedirectToAction(nameof(Checkout), new { addressId = vm.AddressId, couponCode = vm.CouponCode, paymentMethod = vm.PaymentMethod });
            }

            _unitOfWork.Addresses.Remove(address);
            await _unitOfWork.SaveAsync();

            var remainingAddresses = (await _unitOfWork.Addresses.FindAllAsync(a => a.UserId == userId)).ToList();
            if (remainingAddresses.Any() && !remainingAddresses.Any(a => a.IsDefault))
            {
                remainingAddresses[0].IsDefault = true;
                _unitOfWork.Addresses.Update(remainingAddresses[0]);
                await _unitOfWork.SaveAsync();
            }

            TempData["success"] = _localizer["AddressDeletedSuccessfully"].Value;

            return RedirectToAction(nameof(Checkout), new
            {
                addressId = remainingAddresses.FirstOrDefault(a => a.IsDefault)?.Id ?? remainingAddresses.FirstOrDefault()?.Id,
                couponCode = vm.CouponCode,
                paymentMethod = vm.PaymentMethod,
                showNewAddressForm = !remainingAddresses.Any()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteAddress failed. AddressId={AddressId}", deleteAddressId);
            TempData["error"] = _localizer["SomethingWentWrongWhileDeletingAddress"].Value;
            return RedirectToAction("Error", "Home", new { area = "Customer" });
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  POST /Customer/Orders/PlaceOrder
    // ──────────────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceOrder(CheckoutVM vm)
    {
        try
        {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();
        vm.PaymentMethod = NormalizePaymentMethod(vm.PaymentMethod);
        vm.CouponCode = vm.CouponCode?.Trim();
        var addressId = vm.AddressId ?? 0;
        var existingAddresses = await _unitOfWork.Addresses.FindAllAsync(a => a.UserId == userId);

        _logger.LogInformation("PlaceOrder started. UserId={UserId}, AddressId={AddressId}, Coupon={Coupon}, PaymentMethod={PaymentMethod}",
            userId, addressId, vm.CouponCode ?? "none", vm.PaymentMethod);

        var wantsInlineAddress = !existingAddresses.Any() || (vm.ShowNewAddressForm && vm.HasNewAddressInput);

        if (wantsInlineAddress)
        {
            if (!TryValidateInlineAddress(vm))
            {
                vm.ShowNewAddressForm = true;
                StoreCheckoutState(vm);
                TempData["error"] = _localizer["PleaseCompleteNewAddressBeforePlacingOrder"].Value;
                return RedirectToAction(nameof(Checkout));
            }

            var makeDefault = !existingAddresses.Any() || vm.SaveNewAddress;
            var inlineAddress = await CreateInlineAddressAsync(userId, vm, makeDefault);
            addressId = inlineAddress.Id;
            vm.AddressId = addressId;
        }
        else if (addressId <= 0)
        {
            StoreCheckoutState(vm);
            TempData["error"] = _localizer["PleaseSelectDeliveryAddress"].Value;
            return RedirectToAction(nameof(Checkout));
        }

        var userAddresses = existingAddresses.ToList();
        if (userAddresses.Any())
        {
            var changedDefault = false;
            foreach (var existingAddress in userAddresses)
            {
                var shouldBeDefault = existingAddress.Id == addressId;
                if (existingAddress.IsDefault != shouldBeDefault)
                {
                    existingAddress.IsDefault = shouldBeDefault;
                    _unitOfWork.Addresses.Update(existingAddress);
                    changedDefault = true;
                }
            }

            if (changedDefault)
            {
                await _unitOfWork.SaveAsync();
            }
        }

        // ── Step 1: Validate Address ─────────────────────────────────────────
        var address = await _unitOfWork.Addresses.FindAsync(
            a => a.Id == addressId && a.UserId == userId);

        if (address == null)
        {
            _logger.LogWarning("PlaceOrder blocked: Address {AddressId} not found for user {UserId}.",
                addressId, userId);
            StoreCheckoutState(vm);
            TempData["error"] = _localizer["SelectedAddressInvalid"].Value;
            return RedirectToAction(nameof(Checkout));
        }

        // ── Step 2: Load Cart ─────────────────────────────────────────────────
        var cart = await _unitOfWork.Carts.GetCartByUserIdAsync(userId);
        if (cart == null || !cart.Items.Any())
        {
            TempData["Info"] = _localizer["CartEmpty"].Value;
            return RedirectToAction("Index", "Cart");
        }

        // ── Steps 3 & 4: Server-side stock check + subtotal ───────────────────
        decimal subtotal = 0;
        var bundleVariantLookup = await BuildBundleVariantLookupAsync(cart.Items, tracked: false);
        foreach (var item in cart.Items)
        {
            if (item.GiftBundleId.HasValue)
            {
                var bundleSnapshotItems = GiftBundleSnapshotHelper.Deserialize(item.GiftBundleItemsJson);
                if (bundleSnapshotItems.Count == 0)
                {
                    _logger.LogError("PlaceOrder: Bundle CartItem {ItemId} has no valid snapshot. UserId={UserId}.", item.Id, userId);
                    TempData["Error"] = "A gift bundle in your cart is no longer available.";
                    return RedirectToAction("Index", "Cart");
                }

                foreach (var bundleSnapshotItem in bundleSnapshotItems)
                {
                    if (!bundleVariantLookup.TryGetValue(bundleSnapshotItem.ProductVariantId, out var variant) || !variant.IsActive)
                    {
                        TempData["Error"] = $"'{bundleSnapshotItem.ProductName}' is no longer available inside this gift bundle.";
                        return RedirectToAction("Index", "Cart");
                    }

                    if (item.Quantity > variant.Stock)
                    {
                        TempData["Error"] = $"Only {variant.Stock} bundle(s) are available right now because '{bundleSnapshotItem.ProductName}' is low in stock.";
                        return RedirectToAction("Index", "Cart");
                    }
                }

                subtotal += item.Quantity * item.PriceSnapshot;
                continue;
            }

            // Guard: variant may be null if data integrity is broken
            if (item.ProductVariant == null)
            {
                _logger.LogError("PlaceOrder: CartItem {ItemId} has no ProductVariant. UserId={UserId}.",
                    item.Id, userId);
                TempData["Error"] = _localizer["ProductsInCartNoLongerAvailable"].Value;
                return RedirectToAction("Index", "Cart");
            }

            if (!item.ProductVariant.IsActive)
            {
                TempData["Error"] = _localizer["VariantNoLongerAvailable",
                    item.ProductVariant.Product?.Name ?? _localizer["ProductNotFound"].Value,
                    item.ProductVariant.Size,
                    item.ProductVariant.Color].Value;
                return RedirectToAction("Index", "Cart");
            }

            if (item.Quantity > item.ProductVariant.Stock)
            {
                TempData["Error"] = _localizer["NotEnoughStockForVariant",
                    item.ProductVariant.Product?.Name ?? _localizer["ProductNotFound"].Value,
                    item.ProductVariant.Size,
                    item.ProductVariant.Color,
                    item.ProductVariant.Stock,
                    item.Quantity].Value;
                return RedirectToAction("Index", "Cart");
            }

            subtotal += item.Quantity * item.PriceSnapshot;
        }

        // ── Step 5: Validate & Apply Coupon ──────────────────────────────────
        var (appliedCoupon, discountAmount, couponError, appliedCode) = await ResolveCouponAsync(userId, vm.CouponCode, subtotal);
        var couponCode = appliedCode ?? vm.CouponCode?.Trim();
        if (!string.IsNullOrWhiteSpace(couponError))
        {
            _logger.LogWarning("PlaceOrder: Coupon validation failed. Coupon={Coupon}, UserId={UserId}, Reason={Reason}",
                couponCode ?? "none", userId, couponError);
            vm.CouponCode = couponCode;
            vm.AddressId = addressId;
            StoreCheckoutState(vm);
            TempData["error"] = couponError;
            return RedirectToAction(nameof(Checkout));
        }

        if (appliedCoupon != null)
        {
            _logger.LogInformation("PlaceOrder: Coupon '{Coupon}' applied. Discount={Discount:C}. UserId={UserId}.",
                appliedCoupon.CouponCode, discountAmount, userId);
        }

        var totalAmount = subtotal - discountAmount;

        if (vm.PaymentMethod == PaymentMethodCreditCard)
        {
            if (!IsStripeConfigured())
            {
                StoreCheckoutState(vm);
                TempData["error"] = _localizer["StripeNotConfigured"].Value;
                return RedirectToAction(nameof(Checkout));
            }

            try
            {
                var session = await CreateStripeCheckoutSessionAsync(cart, userId, addressId, couponCode, subtotal, discountAmount, totalAmount);
                if (string.IsNullOrWhiteSpace(session.Url))
                {
                    StoreCheckoutState(vm);
                    TempData["error"] = _localizer["StripeSessionCouldNotStart"].Value;
                    return RedirectToAction(nameof(Checkout));
                }

                return Redirect(session.Url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PlaceOrder failed while creating Stripe session. UserId={UserId}.", userId);
                TempData["error"] = _localizer["CardPaymentUnavailable"].Value;
                StoreCheckoutState(vm);
                return RedirectToAction(nameof(Checkout));
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
            TempData["error"] = _localizer["OrderUpdatedWhilePlacing"].Value;
            return RedirectToAction("Index", "Cart");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlaceOrder failed for UserId={UserId}.", userId);
            TempData["error"] = _localizer["SomethingWentWrongWhilePlacingOrder"].Value;
            return RedirectToAction("Error", "Home", new { area = "Customer" });
        }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlaceOrder pipeline failed before completion.");
            TempData["error"] = _localizer["SomethingWentWrongWhilePlacingOrder"].Value;
            return RedirectToAction("Error", "Home", new { area = "Customer" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> StripeSuccess(string sessionId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            TempData["Error"] = _localizer["MissingStripeSessionDetails"].Value;
            return RedirectToAction(nameof(Checkout), new { paymentMethod = PaymentMethodCreditCard });
        }

        if (!IsStripeConfigured())
        {
            TempData["Error"] = _localizer["StripeNotConfigured"].Value;
            return RedirectToAction(nameof(Checkout), new { paymentMethod = PaymentMethodCreditCard });
        }

        try
        {
            var sessionService = new SessionService();
            var session = await sessionService.GetAsync(sessionId);

            if (session == null || !string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = _localizer["StripePaymentNotCompleted"].Value;
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
                TempData["Error"] = _localizer["StripeSessionMissingAddressInformation"].Value;
                return RedirectToAction(nameof(Checkout), new { paymentMethod = PaymentMethodCreditCard });
            }

            metadata.TryGetValue("CouponCode", out var couponCode);

            var address = await _unitOfWork.Addresses.FindAsync(a => a.Id == addressId && a.UserId == userId);
            if (address == null)
            {
                TempData["Error"] = _localizer["SelectedDeliveryAddressNoLongerAvailable"].Value;
                return RedirectToAction(nameof(Checkout), new { couponCode, paymentMethod = PaymentMethodCreditCard });
            }

            var cart = await _unitOfWork.Carts.GetCartByUserIdAsync(userId);
            if (cart == null || !cart.Items.Any())
            {
                TempData["Error"] = _localizer["CouldNotFinalizePaidOrderCartEmpty"].Value;
                return RedirectToAction("Index", "Cart");
            }

            decimal subtotal = 0;
            foreach (var item in cart.Items)
            {
                if (item.ProductVariant == null || !item.ProductVariant.IsActive)
                {
                    TempData["Error"] = _localizer["ProductsInCartNoLongerAvailable"].Value;
                    return RedirectToAction("Index", "Cart");
                }

                if (item.Quantity > item.ProductVariant.Stock)
                {
                    TempData["Error"] = _localizer["NotEnoughStockForProductReviewCart", item.ProductVariant.Product?.Name ?? _localizer["ProductNotFound"].Value].Value;
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
            TempData["Error"] = _localizer["CouldNotFinalizeStripePayment"].Value;
            return RedirectToAction(nameof(Checkout), new { paymentMethod = PaymentMethodCreditCard });
        }
    }

    [HttpGet]
    public IActionResult StripeCancel(string? couponCode = null, int? addressId = null)
    {
        TempData["Error"] = _localizer["CardPaymentCanceled"].Value;
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
            ShippingLabel = string.IsNullOrWhiteSpace(order.Shipment?.Carrier) ? _localizer["StandardShipping"] : order.Shipment.Carrier!,
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

        // Business rule: only Pending and Confirmed orders can be cancelled
        if (order.Status != SD.Status_Pending && order.Status != SD.Status_Confirmed)
        {
            TempData["Error"] = _localizer["OrderCannotBeCancelledStatus", id, order.Status].Value;
            return RedirectToAction(nameof(Details), new { id });
        }

        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            await ReturnStockAsync(order.Id);

            order.Status      = SD.Status_Cancelled;
            order.CancelledAt = DateTime.UtcNow;
            order.UpdatedAt   = DateTime.UtcNow;
            _unitOfWork.Orders.Update(order);

            await _unitOfWork.SaveAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Cancel succeeded. OrderId={OrderId}, UserId={UserId}.", id, userId);
            TempData["Success"] = _localizer["OrderCancelledSuccessfully", id].Value;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex, "Cancel concurrency conflict. OrderId={OrderId}, UserId={UserId}.", id, userId);
            TempData["Error"] = _localizer["OrderCancelConflict"].Value;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Cancel failed. OrderId={OrderId}, UserId={UserId}.", id, userId);
            TempData["Error"] = _localizer["SomethingWentWrongWhileCancellingOrder"].Value;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────────────────────────────────
    private string BuildAddressLine(Address? addr)
    {
        if (addr == null) return _localizer["AddressNotAvailable"];

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

    private async Task ReturnStockAsync(int orderId)
    {
        var items = await _unitOfWork.OrderItems
            .FindAllAsync(i => i.OrderId == orderId);

        foreach (var item in items)
        {
            var variant = await _unitOfWork.ProductVariants
                .GetByIdAsync(item.ProductVariantId, ignoreQueryFilters: true);
            if (variant is null)
            {
                _logger.LogWarning("Cancel: OrderItem {ItemId} in Order {OrderId} has no ProductVariant. Skipping stock restore.",
                    item.Id, orderId);
                continue;
            }

            variant.Stock += item.Quantity;

            if (!variant.IsActive && variant.Stock > 0)
            {
                variant.IsActive = true;
                _logger.LogInformation("Cancel: Variant {VariantId} re-activated after stock restore. OrderId={OrderId}.",
                    variant.Id, orderId);
            }
        }
    }

    private async Task<CheckoutVM> BuildCheckoutViewModelAsync(string userId, CheckoutVM? request = null)
    {
        var cart = await _unitOfWork.Carts.GetCartByUserIdAsync(userId);
        var addresses = await _unitOfWork.Addresses.FindAllAsync(a => a.UserId == userId);
        var bundleVariantLookup = await BuildBundleVariantLookupAsync(cart?.Items ?? [], tracked: false);

        var defaultAddressId = addresses.FirstOrDefault(a => a.IsDefault)?.Id
                               ?? addresses.FirstOrDefault()?.Id;

        var vm = new CheckoutVM
        {
            Items = cart?.Items.Select(ci => MapCheckoutItem(ci, bundleVariantLookup)).ToList() ?? [],
            Subtotal = cart?.Items.Sum(ci => ci.Quantity * ci.PriceSnapshot) ?? 0m,
            Addresses = addresses.Select(a => new AddressOptionCustomerVM
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
            DefaultAddressId   = defaultAddressId,
            AddressId          = request?.AddressId ?? defaultAddressId,
            CouponCode         = request?.CouponCode?.Trim(),
            PaymentMethod      = NormalizePaymentMethod(request?.PaymentMethod),
            ShowNewAddressForm = request?.ShowNewAddressForm ?? !addresses.Any(),
            SaveNewAddress     = request?.SaveNewAddress ?? true,
            NewAddressFullName = request?.NewAddressFullName ?? string.Empty,
            NewAddressPhoneNumber = request?.NewAddressPhoneNumber ?? string.Empty,
            NewAddressStreet   = request?.NewAddressStreet ?? string.Empty,
            NewAddressCity     = request?.NewAddressCity ?? string.Empty,
            NewAddressState    = request?.NewAddressState,
            NewAddressCountry  = string.IsNullOrWhiteSpace(request?.NewAddressCountry)
                                    ? "Egypt"
                                    : request!.NewAddressCountry,
            NewAddressPostalCode = request?.NewAddressPostalCode ?? string.Empty
        };

        var (_, discountAmount, couponError, appliedCode) = await ResolveCouponAsync(userId, vm.CouponCode, vm.Subtotal);
        vm.DiscountAmount = discountAmount;
        vm.CouponCode = appliedCode ?? vm.CouponCode;
        vm.CouponApplied = string.IsNullOrWhiteSpace(couponError) && discountAmount > 0 && !string.IsNullOrWhiteSpace(vm.CouponCode);
        vm.CouponMessage = couponError ?? (vm.CouponApplied ? _localizer["CouponNamedAppliedSuccessfully", vm.CouponCode ?? string.Empty].Value : null);

        return vm;
    }

    private bool TryValidateInlineAddress(CheckoutVM vm)
    {
        var isValid = true;

        if (string.IsNullOrWhiteSpace(vm.NewAddressFullName))
        {
            ModelState.AddModelError(nameof(vm.NewAddressFullName), _localizer["FullNameRequired"]);
            isValid = false;
        }

        if (string.IsNullOrWhiteSpace(vm.NewAddressPhoneNumber))
        {
            ModelState.AddModelError(nameof(vm.NewAddressPhoneNumber), _localizer["PhoneNumberRequired"]);
            isValid = false;
        }

        if (string.IsNullOrWhiteSpace(vm.NewAddressStreet))
        {
            ModelState.AddModelError(nameof(vm.NewAddressStreet), _localizer["StreetAddressRequired"]);
            isValid = false;
        }

        if (string.IsNullOrWhiteSpace(vm.NewAddressCity))
        {
            ModelState.AddModelError(nameof(vm.NewAddressCity), _localizer["CityRequired"]);
            isValid = false;
        }

        if (string.IsNullOrWhiteSpace(vm.NewAddressCountry))
        {
            ModelState.AddModelError(nameof(vm.NewAddressCountry), _localizer["CountryRequired"]);
            isValid = false;
        }

        if (string.IsNullOrWhiteSpace(vm.NewAddressPostalCode))
        {
            ModelState.AddModelError(nameof(vm.NewAddressPostalCode), _localizer["PostalCodeRequired"]);
            isValid = false;
        }

        return isValid;
    }

    private async Task<Address> CreateInlineAddressAsync(string userId, CheckoutVM vm, bool makeDefault)
    {
        if (makeDefault)
        {
            var existingDefaults = await _unitOfWork.Addresses
                .FindAllAsync(a => a.UserId == userId && a.IsDefault);

            foreach (var existing in existingDefaults)
            {
                existing.IsDefault = false;
                _unitOfWork.Addresses.Update(existing);
            }
        }

        var address = new Address
        {
            UserId = userId,
            FullName = vm.NewAddressFullName.Trim(),
            PhoneNumber = vm.NewAddressPhoneNumber.Trim(),
            Street = vm.NewAddressStreet.Trim(),
            City = vm.NewAddressCity.Trim(),
            State = string.IsNullOrWhiteSpace(vm.NewAddressState) ? null : vm.NewAddressState.Trim(),
            Country = vm.NewAddressCountry.Trim(),
            PostalCode = vm.NewAddressPostalCode.Trim(),
            IsDefault = makeDefault
        };

        await _unitOfWork.Addresses.AddAsync(address);
        await _unitOfWork.SaveAsync();
        return address;
    }

    private bool IsStripeConfigured()
    {
        return !string.IsNullOrWhiteSpace(_stripeSettings.SecretKey);
    }

    private void StoreCheckoutState(CheckoutVM vm)
    {
        var state = new CheckoutVM
        {
            AddressId = vm.AddressId,
            CouponCode = vm.CouponCode?.Trim(),
            PaymentMethod = NormalizePaymentMethod(vm.PaymentMethod),
            ShowNewAddressForm = vm.ShowNewAddressForm,
            SaveNewAddress = vm.SaveNewAddress,
            NewAddressFullName = vm.NewAddressFullName ?? string.Empty,
            NewAddressPhoneNumber = vm.NewAddressPhoneNumber ?? string.Empty,
            NewAddressStreet = vm.NewAddressStreet ?? string.Empty,
            NewAddressCity = vm.NewAddressCity ?? string.Empty,
            NewAddressState = vm.NewAddressState,
            NewAddressCountry = vm.NewAddressCountry ?? string.Empty,
            NewAddressPostalCode = vm.NewAddressPostalCode ?? string.Empty
        };

        TempData[CheckoutStateTempDataKey] = JsonSerializer.Serialize(state);
    }

    private CheckoutVM? ReadCheckoutState()
    {
        if (!TempData.TryGetValue(CheckoutStateTempDataKey, out var raw) || raw is not string json || string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CheckoutVM>(json);
        }
        catch
        {
            return null;
        }
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
        var baseUrl = (_configuration["App:PublicBaseUrl"] ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/');
        var successUrl = $"{baseUrl}{Url.Action(nameof(StripeSuccess), "Orders", new { area = "Customer" })}?sessionId={{CHECKOUT_SESSION_ID}}";
        var cancelUrl = $"{baseUrl}{Url.Action(nameof(StripeCancel), "Orders", new { area = "Customer", couponCode, addressId })}";

        _logger.LogInformation(
            "Creating Stripe checkout session. BaseUrl={BaseUrl}, SuccessUrl={SuccessUrl}, CancelUrl={CancelUrl}, UserId={UserId}, AddressId={AddressId}.",
            baseUrl,
            successUrl,
            cancelUrl,
            userId,
            addressId);

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
                        Name = _localizer["SmartStoreOrder"],
                        Description = string.IsNullOrWhiteSpace(couponCode)
                            ? _localizer["FinalOrderTotal"]
                            : _localizer["FinalOrderTotalAfterCoupon", couponCode]
                    }
                }
            });
        }
        else
        {
            foreach (var item in cart.Items)
            {
                var lineName = item.GiftBundleId.HasValue
                    ? item.GiftBundleTitle ?? _localizer["SmartStoreOrder"].Value
                    : item.ProductVariant?.Product?.Name ?? _localizer["SmartStoreOrder"].Value;

                var lineDescription = item.GiftBundleId.HasValue
                    ? string.Join(", ", GiftBundleSnapshotHelper.Deserialize(item.GiftBundleItemsJson).Select(bundleItem => bundleItem.ProductName))
                    : $"{item.ProductVariant?.Size} / {item.ProductVariant?.Color}";

                lineItems.Add(new SessionLineItemOptions
                {
                    Quantity = item.Quantity,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "egp",
                        UnitAmount = (long)Math.Round(item.PriceSnapshot * 100m, MidpointRounding.AwayFromZero),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = lineName,
                            Description = lineDescription
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
            var orderItems = new List<OrderItem>(cart.Items.Count * 3);
            var bundleVariantLookup = await BuildBundleVariantLookupAsync(cart.Items, tracked: true);

            foreach (var item in cart.Items)
            {
                if (item.GiftBundleId.HasValue)
                {
                    var bundleSnapshotItems = GiftBundleSnapshotHelper.Deserialize(item.GiftBundleItemsJson);
                    if (bundleSnapshotItems.Count == 0)
                    {
                        throw new InvalidOperationException("A gift bundle in the cart has no valid snapshot.");
                    }

                    var remainingBundleUnitPrice = item.PriceSnapshot;
                    var originalBundleTotal = bundleSnapshotItems.Sum(bundleItem => bundleItem.UnitPrice);
                    var fallbackUnitShare = bundleSnapshotItems.Count == 0
                        ? 0m
                        : Math.Round(item.PriceSnapshot / bundleSnapshotItems.Count, 2, MidpointRounding.AwayFromZero);

                    for (var index = 0; index < bundleSnapshotItems.Count; index++)
                    {
                        var bundleSnapshotItem = bundleSnapshotItems[index];
                        if (!bundleVariantLookup.TryGetValue(bundleSnapshotItem.ProductVariantId, out var trackedVariant))
                        {
                            throw new InvalidOperationException($"Bundle variant {bundleSnapshotItem.ProductVariantId} could not be loaded.");
                        }

                        trackedVariant.Stock -= item.Quantity;
                        if (trackedVariant.Stock == 0)
                        {
                            trackedVariant.IsActive = false;
                            _logger.LogInformation(
                                "FinalizeOrder: Bundle variant {VariantId} will be deactivated (stock → 0). UserId={UserId}.",
                                trackedVariant.Id, userId);
                        }

                        _unitOfWork.ProductVariants.Update(trackedVariant);

                        decimal unitPriceShare;
                        if (index == bundleSnapshotItems.Count - 1)
                        {
                            unitPriceShare = Math.Max(0, Math.Round(remainingBundleUnitPrice, 2, MidpointRounding.AwayFromZero));
                        }
                        else if (originalBundleTotal > 0)
                        {
                            unitPriceShare = Math.Round(
                                item.PriceSnapshot * (bundleSnapshotItem.UnitPrice / originalBundleTotal),
                                2,
                                MidpointRounding.AwayFromZero);
                            remainingBundleUnitPrice -= unitPriceShare;
                        }
                        else
                        {
                            unitPriceShare = fallbackUnitShare;
                            remainingBundleUnitPrice -= unitPriceShare;
                        }

                        orderItems.Add(new OrderItem
                        {
                            ProductVariantId = trackedVariant.Id,
                            ProductName = $"{bundleSnapshotItem.ProductName} ({item.GiftBundleTitle ?? "Gift Bundle"})",
                            Size = bundleSnapshotItem.Size,
                            Color = bundleSnapshotItem.Color,
                            Quantity = item.Quantity,
                            UnitPrice = unitPriceShare,
                            Subtotal = unitPriceShare * item.Quantity
                        });
                    }

                    continue;
                }

                if (item.ProductVariant == null)
                {
                    throw new InvalidOperationException($"CartItem {item.Id} has no ProductVariant.");
                }

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
                    ProductVariantId = item.ProductVariant.Id,
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

            var customerEmail = order.User?.Email;
            if (string.IsNullOrWhiteSpace(customerEmail))
            {
                customerEmail = (await _userManager.FindByIdAsync(userId))?.Email;
            }

            if (!string.IsNullOrWhiteSpace(customerEmail))
            {
                // Send order confirmation email — fire and forget, do not await to avoid blocking
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var emailBody = $"""
                            <h2>Order Confirmed — #{order.Id}</h2>
                            <p>Thank you for your order!</p>
                            <p><strong>Total:</strong> {order.TotalAmount:C}</p>
                            <p><strong>Status:</strong> {order.Status}</p>
                            <p>We will notify you when your order ships.</p>
                            """;
                        await _emailSender.SendEmailAsync(
                            customerEmail,
                            $"Smart Store — Order #{order.Id} Confirmed",
                            emailBody);
                    }
                    catch { /* email failure must never break order flow */ }
                });
            }

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
            return (null, 0m, _localizer["CouponInvalidOrDeactivated", couponCode], normalizedCode);
        }

        if (appliedCoupon.ExpiresAt.HasValue && appliedCoupon.ExpiresAt.Value < DateTime.UtcNow)
        {
            return (null, 0m, _localizer["CouponHasExpired", appliedCoupon.CouponCode], appliedCoupon.CouponCode);
        }

        if (appliedCoupon.UsageLimit.HasValue && appliedCoupon.UsageCount >= appliedCoupon.UsageLimit.Value)
        {
            return (null, 0m, _localizer["CouponNamedUsageLimitReached", appliedCoupon.CouponCode], appliedCoupon.CouponCode);
        }

        var alreadyUsedByCustomer = await _unitOfWork.Orders
            .Query()
            .AnyAsync(o => o.UserId == userId && o.CouponCode == appliedCoupon.CouponCode);

        if (alreadyUsedByCustomer)
        {
            return (null, 0m, _localizer["CouponAlreadyUsed", appliedCoupon.CouponCode], appliedCoupon.CouponCode);
        }

        if (appliedCoupon.MinimumOrderAmount.HasValue && subtotal < appliedCoupon.MinimumOrderAmount.Value)
        {
            return (null, 0m,
                _localizer["CouponMinimumOrderRequiredDetailed",
                    appliedCoupon.MinimumOrderAmount.Value.ToString("C", System.Globalization.CultureInfo.CurrentCulture),
                    subtotal.ToString("C", System.Globalization.CultureInfo.CurrentCulture)],
                appliedCoupon.CouponCode);
        }

        var discountAmount = appliedCoupon.Type == SD.Discount_Percentage
            ? subtotal * (appliedCoupon.Value / 100m)
            : appliedCoupon.Value;

        discountAmount = Math.Min(discountAmount, subtotal);

        return (appliedCoupon, discountAmount, null, appliedCoupon.CouponCode);
    }

    private CheckoutItemCustomerVM MapCheckoutItem(CartItem cartItem, IReadOnlyDictionary<int, ProductVariant> bundleVariantLookup)
    {
        if (cartItem.GiftBundleId.HasValue)
        {
            var bundleItems = GiftBundleSnapshotHelper.Deserialize(cartItem.GiftBundleItemsJson);
            return new CheckoutItemCustomerVM
            {
                CartItemId = cartItem.Id,
                GiftBundleId = cartItem.GiftBundleId,
                GiftBundleTitle = cartItem.GiftBundleTitle,
                GiftBundleOriginalTotal = cartItem.GiftBundleOriginalTotal,
                Quantity = cartItem.Quantity,
                PriceSnapshot = cartItem.PriceSnapshot,
                CurrentPrice = cartItem.PriceSnapshot,
                ProductName = cartItem.GiftBundleTitle ?? "Gift Bundle",
                ImageUrl = bundleItems.FirstOrDefault()?.ImageUrl,
                Stock = ResolveBundleStock(bundleItems, bundleVariantLookup),
                BundleItems = bundleItems.Select(bundleItem => new GiftBundleCheckoutProductVM
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
            return new CheckoutItemCustomerVM
            {
                CartItemId = cartItem.Id,
                Quantity = cartItem.Quantity,
                PriceSnapshot = cartItem.PriceSnapshot,
                CurrentPrice = cartItem.PriceSnapshot,
                ProductName = "Unavailable item",
                Stock = 0
            };
        }

        return new CheckoutItemCustomerVM
        {
            CartItemId = cartItem.Id,
            ProductVariantId = cartItem.ProductVariantId,
            Quantity = cartItem.Quantity,
            PriceSnapshot = cartItem.PriceSnapshot,
            CurrentPrice = cartItem.PriceSnapshot,
            ProductName = cartItem.ProductVariant.Product.Name,
            Size = cartItem.ProductVariant.Size,
            Color = cartItem.ProductVariant.Color,
            ImageUrl = cartItem.ProductVariant.Product.Images
                         .FirstOrDefault(i => i.IsMain)?.ImageUrl
                       ?? cartItem.ProductVariant.Product.Images
                         .OrderBy(i => i.DisplayOrder)
                         .FirstOrDefault()?.ImageUrl,
            Stock = cartItem.ProductVariant.Stock
        };
    }

    private async Task<IReadOnlyDictionary<int, ProductVariant>> BuildBundleVariantLookupAsync(IEnumerable<CartItem> cartItems, bool tracked)
    {
        var variantIds = cartItems
            .Where(item => item.GiftBundleId.HasValue)
            .SelectMany(item => GiftBundleSnapshotHelper.Deserialize(item.GiftBundleItemsJson))
            .Select(item => item.ProductVariantId)
            .Distinct()
            .ToList();

        if (variantIds.Count == 0)
        {
            return new Dictionary<int, ProductVariant>();
        }

        return (await _unitOfWork.ProductVariants
            .FindAllAsync(variant => variantIds.Contains(variant.Id), tracked: tracked, ignoreQueryFilters: true))
            .ToDictionary(variant => variant.Id, variant => variant);
    }

    private static int ResolveBundleStock(IEnumerable<GiftBundleSnapshotItem> bundleItems, IReadOnlyDictionary<int, ProductVariant> variantLookup)
    {
        var stocks = bundleItems
            .Select(bundleItem => variantLookup.TryGetValue(bundleItem.ProductVariantId, out var variant) ? variant.Stock : 0)
            .ToList();

        return stocks.Count == 0 ? 0 : stocks.Min();
    }
}
