using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ECommerce_System.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SD.Role_Admin)]
public class OrdersController : Controller
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<ApplicationUser> _userManager;

    public OrdersController(IUnitOfWork unitOfWork, UserManager<ApplicationUser> userManager)
    {
        _unitOfWork  = unitOfWork;
        _userManager = userManager;
    }

    // ──────────────────────────────────────────────────────────
    //  GET /Admin/Orders  –  List with optional status filter
    // ──────────────────────────────────────────────────────────
    public async Task<IActionResult> Index(string? status)
    {
        var orders = await _unitOfWork.Orders
            .FindAllAsync(o => true, includeProperties: "User,Address,OrderItems,Shipment", tracked: false);

        // Filter by status if provided
        if (!string.IsNullOrWhiteSpace(status))
            orders = orders.Where(o => o.Status.Equals(status, StringComparison.OrdinalIgnoreCase));

        var viewModels = orders
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderIndexVM
            {
                Id             = o.Id,
                CustomerName   = o.User?.FullName  ?? o.Address?.FullName ?? "Unknown",
                CustomerEmail  = o.User?.Email     ?? string.Empty,
                ItemCount      = o.OrderItems?.Count ?? 0,
                TotalAmount    = o.TotalAmount,
                Status         = o.Status,
                PaymentStatus  = o.PaymentStatus,
                CreatedAt      = o.CreatedAt,
                ShipmentId     = o.Shipment?.Id,
                ShipmentStatus = o.Shipment?.Status
            })
            .ToList();

        ViewBag.CurrentStatus = status;
        ViewBag.AllStatuses   = UpdateOrderStatusVM.OrderStatuses;
        return View(viewModels);
    }

    // ──────────────────────────────────────────────────────────
    //  GET /Admin/Orders/Details/{id}
    // ──────────────────────────────────────────────────────────
    public async Task<IActionResult> Details(int id)
    {
        var order = await _unitOfWork.Orders.GetOrderWithDetailsAsync(id);
        if (order is null) return NotFound();

        var vm = MapToDetailsVM(order);
        return View(vm);
    }

    // ──────────────────────────────────────────────────────────
    //  GET /Admin/Orders/UpdateStatus/{id}
    // ──────────────────────────────────────────────────────────
    public async Task<IActionResult> UpdateStatus(int id)
    {
        var order = await _unitOfWork.Orders.GetByIdAsync(id);
        if (order is null) return NotFound();

        var vm = new UpdateOrderStatusVM
        {
            OrderId               = order.Id,
            CurrentStatus         = order.Status,
            NewStatus             = order.Status,
            CurrentPaymentStatus  = order.PaymentStatus,
            NewPaymentStatus      = order.PaymentStatus
        };
        return View(vm);
    }

    // ──────────────────────────────────────────────────────────
    //  POST /Admin/Orders/UpdateStatus
    // ──────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(UpdateOrderStatusVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var order = await _unitOfWork.Orders.GetByIdAsync(vm.OrderId);
        if (order is null) return NotFound();

        // Apply changes
        order.Status        = vm.NewStatus;
        order.PaymentStatus = vm.NewPaymentStatus;
        order.UpdatedAt     = DateTime.UtcNow;

        // Business rule BR-009: return stock automatically on cancellation
        if (vm.NewStatus == SD.Status_Cancelled && vm.CurrentStatus != SD.Status_Cancelled)
            await ReturnStockAsync(order.Id);

        _unitOfWork.Orders.Update(order);
        await _unitOfWork.SaveAsync();

        TempData["Success"] = $"Order #{order.Id} status updated successfully.";
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    // ──────────────────────────────────────────────────────────
    //  GET /Admin/Orders/Create
    // ──────────────────────────────────────────────────────────
    public async Task<IActionResult> Create()
    {
        await PopulateCreateDropdownsAsync();
        return View(new CreateOrderVM());
    }

    // ──────────────────────────────────────────────────────────
    //  POST /Admin/Orders/Create
    // ──────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateOrderVM vm)
    {
        if (!ModelState.IsValid || vm.Items == null || !vm.Items.Any())
        {
            if (vm.Items == null || !vm.Items.Any())
                ModelState.AddModelError("", "You must add at least one product to the order.");
            
            await PopulateCreateDropdownsAsync();
            return View(vm);
        }

        // 1. Resolve User (Find by phone, or create new guest user)
        var user = _userManager.Users.FirstOrDefault(u => u.PhoneNumber == vm.CustomerPhone || u.Email == $"{vm.CustomerPhone}@guest.local");
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = $"{vm.CustomerPhone}@guest.local",
                Email = $"{vm.CustomerPhone}@guest.local",
                PhoneNumber = vm.CustomerPhone,
                FullName = vm.CustomerName,
                EmailConfirmed = true
            };
            var result = await _userManager.CreateAsync(user, $"Guest@{vm.CustomerPhone}!");
            if (!result.Succeeded)
            {
                ModelState.AddModelError("", "Failed to auto-create customer account.");
                await PopulateCreateDropdownsAsync();
                return View(vm);
            }
        }

        // 2. Resolve Address
        // Address uniqueness could be checked by Street + City + State
        var address = await _unitOfWork.Addresses.FindAsync(a => a.UserId == user.Id && a.Street == vm.ShippingStreet && a.City == vm.ShippingCity);
        if (address is null)
        {
            address = new Address
            {
                UserId = user.Id,
                FullName = vm.CustomerName,
                PhoneNumber = vm.CustomerPhone,
                Street = vm.ShippingStreet,
                City = vm.ShippingCity,
                State = vm.ShippingState,
                Country = vm.ShippingCountry,
                PostalCode = "00000"
            };
            await _unitOfWork.Addresses.AddAsync(address);
            await _unitOfWork.SaveAsync();
        }

        // 3. Process Items & Calculate Subtotal Securely
        decimal trueSubtotal = 0;
        var orderItemsToSave = new List<OrderItem>();

        foreach (var item in vm.Items)
        {
            var variant = await _unitOfWork.ProductVariants.FindAsync(v => v.Id == item.ProductVariantId, includeProperties: "Product");
            if (variant is null)
            {
                ModelState.AddModelError("", $"Product Variant ID {item.ProductVariantId} not found.");
                await PopulateCreateDropdownsAsync();
                return View(vm);
            }

            if (variant.Stock < item.Quantity)
            {
                ModelState.AddModelError("", $"Not enough stock for {variant.Product.Name} - {variant.Size}. Available: {variant.Stock}");
                await PopulateCreateDropdownsAsync();
                return View(vm);
            }

            var itemPrice = variant.Price > 0 ? variant.Price : variant.Product.BasePrice;
            var lineTotal = itemPrice * item.Quantity;
            trueSubtotal += lineTotal;

            orderItemsToSave.Add(new OrderItem
            {
                ProductVariantId = variant.Id,
                ProductName = variant.Product.Name,
                Size = variant.Size,
                Color = variant.Color,
                UnitPrice = itemPrice,
                Quantity = item.Quantity,
                Subtotal = lineTotal
            });

            // Deduct Stock
            variant.Stock -= item.Quantity;
            if (variant.Stock <= 0)
            {
                variant.Stock    = 0; // clamp to 0
                variant.IsActive = false; // auto-deactivate when stock hits 0
            }
            // EF Core Change Tracker detects the changes upon SaveAsync.
        }

        // 4. Calculate Discount Securely
        decimal discountAmount = 0;
        Discount? appliedDiscount = null;

        if (!string.IsNullOrWhiteSpace(vm.CouponCode))
        {
            appliedDiscount = await _unitOfWork.Discounts.FindAsync(d => d.CouponCode == vm.CouponCode && d.IsActive);
            if (appliedDiscount != null)
            {
                if (appliedDiscount.ExpiresAt.HasValue && appliedDiscount.ExpiresAt < DateTime.UtcNow)
                    appliedDiscount = null;
                else if (appliedDiscount.MinimumOrderAmount.HasValue && trueSubtotal < appliedDiscount.MinimumOrderAmount.Value)
                    appliedDiscount = null;
                else if (appliedDiscount.UsageLimit.HasValue && appliedDiscount.UsageCount >= appliedDiscount.UsageLimit.Value)
                    appliedDiscount = null;
            }

            if (appliedDiscount != null)
            {
                if (appliedDiscount.Type == SD.Discount_Percentage)
                    discountAmount = trueSubtotal * (appliedDiscount.Value / 100m);
                else
                    discountAmount = appliedDiscount.Value;

                if (discountAmount > trueSubtotal) discountAmount = trueSubtotal;
                
                appliedDiscount.UsageCount++;
                _unitOfWork.Discounts.Update(appliedDiscount);
            }
        }

        // 5. Create Order
        var order = new Order
        {
            UserId        = user.Id,
            AddressId     = address.Id,
            Status        = vm.Status,
            PaymentStatus = vm.PaymentStatus,
            Subtotal      = trueSubtotal,
            DiscountAmount= discountAmount,
            TotalAmount   = trueSubtotal - discountAmount,
            CouponCode    = appliedDiscount?.CouponCode,
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow,
            OrderItems    = orderItemsToSave 
        };

        await _unitOfWork.Orders.AddAsync(order);
        await _unitOfWork.SaveAsync();

        // Update Product Status internally for all processed items
        foreach (var item in vm.Items)
        {
            var variant = await _unitOfWork.ProductVariants.FindAsync(v => v.Id == item.ProductVariantId);
            if (variant != null)
            {
                await UpdateProductStatusAsync(variant.ProductId);
            }
        }

        TempData["Success"] = $"Order #{order.Id} created successfully for {vm.CustomerName}.";
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    // ──────────────────────────────────────────────────────────
    //  GET /Admin/Orders/ValidateCoupon 
    //  (AJAX Endpoint for dynamic subtotal calculation)
    // ──────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> ValidateCoupon(string code, decimal subtotal)
    {
        var discount = await _unitOfWork.Discounts.FindAsync(d => d.CouponCode == code && d.IsActive);
        
        if (discount == null)
            return Json(new { success = false, message = "Invalid coupon code." });
            
        if (discount.ExpiresAt.HasValue && discount.ExpiresAt < DateTime.UtcNow)
            return Json(new { success = false, message = "Coupon has expired." });
            
        if (discount.MinimumOrderAmount.HasValue && subtotal < discount.MinimumOrderAmount.Value)
            return Json(new { success = false, message = $"Minimum order amount of {discount.MinimumOrderAmount:C} required." });
            
        if (discount.UsageLimit.HasValue && discount.UsageCount >= discount.UsageLimit.Value)
            return Json(new { success = false, message = "Coupon usage limit reached." });

        decimal discountValue = 0;
        if (discount.Type == SD.Discount_Percentage)
            discountValue = subtotal * (discount.Value / 100m);
        else
            discountValue = discount.Value;

        if (discountValue > subtotal) discountValue = subtotal;

        return Json(new { 
            success = true, 
            discountAmount = discountValue,
            message = "Coupon applied! Discount: " + discountValue.ToString("C")
        });
    }

    // ──────────────────────────────────────────────────────────
    //  POST /Admin/Orders/Delete/{id}
    // ──────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var order = await _unitOfWork.Orders.GetByIdAsync(id);
        if (order is null) return NotFound();

        // Return stock if order was not already cancelled
        if (order.Status != SD.Status_Cancelled)
            await ReturnStockAsync(order.Id);

        _unitOfWork.Orders.Remove(order);
        await _unitOfWork.SaveAsync();

        TempData["Success"] = $"Order #{id} deleted successfully.";
        return RedirectToAction(nameof(Index));
    }

    // ──────────────────────────────────────────────────────────
    //  Private helper – Populate Create dropdowns
    // ──────────────────────────────────────────────────────────
    private async Task PopulateCreateDropdownsAsync()
    {
        // Provide all active product variants to the View for selection
        var variants = await _unitOfWork.ProductVariants.FindAllAsync(v => v.IsActive && v.Stock > 0, includeProperties: "Product", tracked: false);
        
        var productList = variants.Select(v => new
        {
            id = v.Id,
            name = $"{v.Product.Name} - {v.Size} ({v.Color})",
            price = v.Price > 0 ? v.Price : v.Product.BasePrice,
            stock = v.Stock
        }).ToList();
        
        ViewBag.ProductsListJson = System.Text.Json.JsonSerializer.Serialize(productList);

        ViewBag.OrderStatuses   = UpdateOrderStatusVM.OrderStatuses;
        ViewBag.PaymentStatuses = UpdateOrderStatusVM.PaymentStatuses;
    }

    // ──────────────────────────────────────────────────────────
    //  Private helper – Build OrderDetailsVM
    // ──────────────────────────────────────────────────────────
    private static OrderDetailsVM MapToDetailsVM(Order order)
    {
        var address = order.Address;
        var addressLine = address is null ? string.Empty
            : $"{address.Street}, {address.City}, {address.State}, {address.Country} {address.PostalCode}";

        ShipmentVM? shipmentVm = null;
        if (order.Shipment is not null)
        {
            var s = order.Shipment;
            shipmentVm = new ShipmentVM
            {
                Id                = s.Id,
                OrderId           = s.OrderId,
                TrackingNumber    = s.TrackingNumber,
                Carrier           = s.Carrier,
                Status            = s.Status,
                EstimatedDelivery = s.EstimatedDelivery,
                ShippedAt         = s.ShippedAt,
                DeliveredAt       = s.DeliveredAt
            };
        }

        return new OrderDetailsVM
        {
            Id             = order.Id,
            CustomerName   = order.User?.FullName   ?? address?.FullName  ?? "Unknown",
            CustomerEmail  = order.User?.Email       ?? string.Empty,
            Status         = order.Status,
            PaymentStatus  = order.PaymentStatus,
            Subtotal       = order.Subtotal,
            DiscountAmount = order.DiscountAmount,
            TotalAmount    = order.TotalAmount,
            CouponCode     = order.CouponCode,
            CreatedAt      = order.CreatedAt,
            AddressLine    = addressLine,
            Shipment       = shipmentVm,
            Items          = order.OrderItems?.Select(i => new OrderItemVM
            {
                ProductName = i.ProductName,
                Size        = i.Size,
                Color       = i.Color,
                Quantity    = i.Quantity,
                UnitPrice   = i.UnitPrice,
                Subtotal    = i.Subtotal
            }).ToList() ?? []
        };
    }

    // ──────────────────────────────────────────────────────────
    //  Private helper – Return stock when cancelling
    // ──────────────────────────────────────────────────────────
    private async Task ReturnStockAsync(int orderId)
    {
        var items = await _unitOfWork.OrderItems
            .FindAllAsync(i => i.OrderId == orderId);

        foreach (var item in items)
        {
            var variant = await _unitOfWork.ProductVariants
                .GetByIdAsync(item.ProductVariantId, ignoreQueryFilters: true);
            if (variant is null) continue;

            variant.Stock += item.Quantity;

            // Auto-reactivate variant if it was deactivated due to zero stock
            if (!variant.IsActive && variant.Stock > 0)
                variant.IsActive = true;

            // EF Change Tracker handles the update automatically
        }
    }

    private async Task UpdateProductStatusAsync(int productId)
    {
        var product = await _unitOfWork.Products
            .FindAsync(p => p.Id == productId, "Variants", ignoreQueryFilters: true);

        if (product is null) return;

        bool hasActiveStock = product.Variants.Any(v => v.IsActive && v.Stock > 0);

        if (!hasActiveStock && product.IsActive)
        {
            product.IsActive = false;
            await _unitOfWork.SaveAsync();
        }
        else if (hasActiveStock && !product.IsActive)
        {
            product.IsActive = true;
            await _unitOfWork.SaveAsync();
        }
    }
}
