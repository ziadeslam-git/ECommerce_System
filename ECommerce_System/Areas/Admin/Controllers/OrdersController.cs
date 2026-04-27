using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ECommerce_System.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SD.Role_Admin)]
public class OrdersController : Controller
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<ApplicationUser> _userManager;
    private const int PageSize = 10;

    public OrdersController(IUnitOfWork unitOfWork, UserManager<ApplicationUser> userManager)
    {
        _unitOfWork  = unitOfWork;
        _userManager = userManager;
    }

    // ──────────────────────────────────────────────────────────
    //  GET /Admin/Orders  –  List with optional status filter
    // ──────────────────────────────────────────────────────────
    public async Task<IActionResult> Index(string? status, int page = 1)
    {
        page = Math.Max(page, 1);
        var cutoff = DateTime.UtcNow.AddHours(-24);

        var query = _unitOfWork.Orders
            .Query()
            .AsNoTracking()
            .Where(o =>
            o.Status != SD.Status_Cancelled ||
            !o.CancelledAt.HasValue ||
            o.CancelledAt.Value > cutoff);

        // Filter by status if provided
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(o => o.Status.Equals(status));

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        if (page > totalPages)
            page = totalPages;

        var orders = await query
            .Include(o => o.User)
            .Include(o => o.Address)
            .Include(o => o.OrderItems)
            .Include(o => o.Shipment)
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var viewModels = orders
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
        ViewBag.CurrentPage   = page;
        ViewBag.TotalPages    = totalPages;
        ViewBag.TotalCount    = totalCount;
        ViewBag.PageSize      = PageSize;
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

        // FIX #3: Enforce legal order status transitions
        if (!IsValidOrderTransition(vm.CurrentStatus, vm.NewStatus))
        {
            TempData["error"] = $"Cannot transition order from {vm.CurrentStatus} to {vm.NewStatus}.";
            return RedirectToAction(nameof(Details), new { id = vm.OrderId });
        }
        if (!IsValidPaymentTransition(vm.CurrentPaymentStatus, vm.NewPaymentStatus))
        {
            TempData["error"] = $"Invalid payment status transition.";
            return RedirectToAction(nameof(Details), new { id = vm.OrderId });
        }

        // Apply changes
        order.Status        = vm.NewStatus;
        order.PaymentStatus = vm.NewPaymentStatus;
        order.UpdatedAt     = DateTime.UtcNow;

        // Business rule BR-009: return stock automatically on cancellation
        if (vm.NewStatus == SD.Status_Cancelled && vm.CurrentStatus != SD.Status_Cancelled)
        {
            await ReturnStockAsync(order.Id);
            order.CancelledAt = DateTime.UtcNow;  // Start 24h countdown
        }

        // Sync payment records (e.g. Unpaid -> Paid creates a record, Pending -> Paid updates it)
        await SyncPaymentRecordAsync(order);

        _unitOfWork.Orders.Update(order);
        await _unitOfWork.SaveAsync();

        TempData["success"] = $"Order #{order.Id} status updated successfully.";
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

        // FIX #2: Wrap all DB operations in a single transaction for atomicity
        using var tx = await _unitOfWork.BeginTransactionAsync();
        try
        {
            // 2. Resolve Address
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
                await _unitOfWork.SaveAsync(); // Need address.Id as FK
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
                    await tx.RollbackAsync();
                    await PopulateCreateDropdownsAsync();
                    return View(vm);
                }

                if (variant.Stock < item.Quantity)
                {
                    ModelState.AddModelError("", $"Not enough stock for {variant.Product.Name} - {variant.Size}. Available: {variant.Stock}");
                    await tx.RollbackAsync();
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
                    variant.Stock    = 0;
                    variant.IsActive = false;
                }
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
                    var alreadyUsed = await _unitOfWork.Orders.FindAsync(o => o.UserId == user.Id && o.CouponCode == appliedDiscount.CouponCode);
                    if (alreadyUsed != null)
                    {
                        ModelState.AddModelError(nameof(vm.CouponCode), "Customer has already used this coupon code.");
                        await tx.RollbackAsync();
                        await PopulateCreateDropdownsAsync();
                        return View(vm);
                    }

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

            // Save first so order.Id is generated as FK for Payment
            await _unitOfWork.SaveAsync();

            // 6. Auto-generate Payment Tracking Record if applicable
            await SyncPaymentRecordAsync(order);

            // Final save + commit transaction
            await _unitOfWork.SaveAsync();
            await tx.CommitAsync();

            // 7. Update Product Status for all processed items (outside tx — non-critical)
            foreach (var item in vm.Items)
            {
                var variant = await _unitOfWork.ProductVariants
                    .FindAsync(v => v.Id == item.ProductVariantId, ignoreQueryFilters: true);
                if (variant != null)
                    await UpdateProductStatusAsync(variant.ProductId);
            }

            TempData["success"] = $"Order #{order.Id} created successfully for {vm.CustomerName}.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ──────────────────────────────────────────────────────────
    //  GET /Admin/Orders/Edit/{id}
    // ──────────────────────────────────────────────────────────
    public async Task<IActionResult> Edit(int id)
    {
        var order = await _unitOfWork.Orders.GetOrderWithDetailsAsync(id);
        if (order is null) return NotFound();

        var address = order.Address;
        var addressLine = address is null ? string.Empty
            : $"{address.Street}, {address.City}, {address.State}, {address.Country}";

        var vm = new EditOrderVM
        {
            OrderId       = order.Id,
            CustomerName  = order.User?.FullName ?? address?.FullName ?? "Unknown",
            CustomerEmail = order.User?.Email    ?? string.Empty,
            CurrentStatus = order.Status,
            AddressLine   = addressLine,
            CouponCode    = order.CouponCode,
            ExistingItems = order.OrderItems?.Select(i => new EditOrderItemVM
            {
                OrderItemId      = i.Id,
                ProductVariantId = i.ProductVariantId,
                ProductName      = i.ProductName,
                Size             = i.Size,
                Color            = i.Color,
                UnitPrice        = i.UnitPrice,
                OriginalQuantity = i.Quantity,
                Quantity         = i.Quantity
            }).ToList() ?? []
        };

        await PopulateCreateDropdownsAsync();
        ViewData["Title"] = $"Edit Order #{id}";
        return View(vm);
    }

    // ──────────────────────────────────────────────────────────
    //  POST /Admin/Orders/Edit/{id}
    // ──────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditOrderVM vm)
    {
        if (id != vm.OrderId) return BadRequest();

        var order = await _unitOfWork.Orders.GetOrderWithDetailsAsync(id);
        if (order is null) return NotFound();

        // FIX #1: Admin has FULL edit access regardless of status
        // (the view still shows a warning banner when editing Shipped/Delivered/Cancelled,
        //  but it no longer blocks the save)

        // FIX #3: Wrap in transaction for atomicity
        using var tx = await _unitOfWork.BeginTransactionAsync();
        try
        {

        // ── 1. Update EXISTING items (quantity change or remove if 0) ──
        if (vm.ExistingItems != null)
        {
            foreach (var editedItem in vm.ExistingItems)
            {
                var dbItem = await _unitOfWork.OrderItems
                    .FindAsync(oi => oi.Id == editedItem.OrderItemId);
                if (dbItem is null) continue;

                var variant = await _unitOfWork.ProductVariants
                    .GetByIdAsync(dbItem.ProductVariantId, ignoreQueryFilters: true);
                if (variant is null) continue;

                int diff = editedItem.Quantity - dbItem.Quantity;

                if (editedItem.Quantity <= 0)
                {
                    // Remove item → restore full stock
                    variant.Stock += dbItem.Quantity;
                    if (!variant.IsActive && variant.Stock > 0) variant.IsActive = true;
                    _unitOfWork.OrderItems.Remove(dbItem);
                }
                else if (diff != 0)
                {
                    // Changed quantity
                    if (diff > 0 && variant.Stock < diff)
                    {
                        TempData["error"] = $"Not enough stock to increase {dbItem.ProductName} - {dbItem.Size}. Available extra: {variant.Stock}";
                        await PopulateCreateDropdownsAsync();
                        ViewData["Title"] = $"Edit Order #{id}";
                        return View(vm);
                    }
                    variant.Stock        -= diff;  // negative diff = returning stock
                    if (variant.Stock < 0) variant.Stock = 0;
                    if (variant.Stock <= 0)  variant.IsActive = false;
                    else if (!variant.IsActive) variant.IsActive = true;
                    dbItem.Quantity  = editedItem.Quantity;
                    dbItem.Subtotal  = dbItem.UnitPrice * editedItem.Quantity;
                }
            }
        }

        // ── 2. Add NEW items ──
        if (vm.NewItems != null && vm.NewItems.Any(i => i.ProductVariantId > 0 && i.Quantity > 0))
        {
            foreach (var newItem in vm.NewItems.Where(i => i.ProductVariantId > 0 && i.Quantity > 0))
            {

                var variant = await _unitOfWork.ProductVariants
                    .FindAsync(v => v.Id == newItem.ProductVariantId, "Product");
                if (variant is null) continue;

                if (variant.Stock < newItem.Quantity)
                {
                    TempData["error"] = $"Not enough stock for {variant.Product.Name} - {variant.Size}. Available: {variant.Stock}";
                    await PopulateCreateDropdownsAsync();
                    ViewData["Title"] = $"Edit Order #{id}";
                    return View(vm);
                }

                var unitPrice = variant.Price > 0 ? variant.Price : variant.Product.BasePrice;

                // Check if variant already in order — increase quantity instead
                var existingItem = order.OrderItems?
                    .FirstOrDefault(oi => oi.ProductVariantId == newItem.ProductVariantId);

                if (existingItem != null)
                {
                    existingItem.Quantity += newItem.Quantity;
                    existingItem.Subtotal  = existingItem.UnitPrice * existingItem.Quantity;
                }
                else
                {
                    var orderItem = new OrderItem
                    {
                        OrderId          = order.Id,
                        ProductVariantId = variant.Id,
                        ProductName      = variant.Product.Name,
                        Size             = variant.Size,
                        Color            = variant.Color,
                        UnitPrice        = unitPrice,
                        Quantity         = newItem.Quantity,
                        Subtotal         = unitPrice * newItem.Quantity
                    };
                    await _unitOfWork.OrderItems.AddAsync(orderItem);
                }

                // Deduct stock
                variant.Stock -= newItem.Quantity;
                if (variant.Stock <= 0)
                {
                    variant.Stock    = 0;
                    variant.IsActive = false;
                }
            }
        }

        // Recalculate totals
        await _unitOfWork.SaveAsync(); // save new items first so we can reload

        var reloadedOrder = await _unitOfWork.Orders.GetOrderWithDetailsAsync(id);
        if (reloadedOrder is null) return NotFound();

        decimal newSubtotal = reloadedOrder.OrderItems?.Sum(i => i.Subtotal) ?? 0;
        decimal newDiscount = 0;

        // Re-apply coupon if present (keep existing logic)
        if (!string.IsNullOrWhiteSpace(reloadedOrder.CouponCode))
        {
            var disc = await _unitOfWork.Discounts
                .FindAsync(d => d.CouponCode == reloadedOrder.CouponCode && d.IsActive);
            if (disc != null)
            {
                newDiscount = disc.Type == SD.Discount_Percentage
                    ? newSubtotal * (disc.Value / 100m)
                    : disc.Value;
                if (newDiscount > newSubtotal) newDiscount = newSubtotal;
            }
        }

        reloadedOrder.Subtotal       = newSubtotal;
        reloadedOrder.DiscountAmount = newDiscount;
        reloadedOrder.TotalAmount    = newSubtotal - newDiscount;
        reloadedOrder.UpdatedAt      = DateTime.UtcNow;

        await _unitOfWork.SaveAsync();
        
        await SyncPaymentRecordAsync(reloadedOrder);

        // Update product statuses
        if (reloadedOrder.OrderItems != null)
        {
            foreach (var oi in reloadedOrder.OrderItems)
            {
                var v = await _unitOfWork.ProductVariants
                    .FindAsync(pv => pv.Id == oi.ProductVariantId, ignoreQueryFilters: true);
                if (v != null) await UpdateProductStatusAsync(v.ProductId);
            }
        }

        await tx.CommitAsync();
        TempData["success"] = $"Order #{id} updated successfully. New total: {reloadedOrder.TotalAmount:C}";
        return RedirectToAction(nameof(Details), new { id });
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ──────────────────────────────────────────────────────────
    //  GET /Admin/Orders/ValidateCoupon 
    //  (AJAX Endpoint for dynamic subtotal calculation)
    // ──────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> ValidateCoupon(string code, decimal subtotal, string? phone)
    {
        var discount = await _unitOfWork.Discounts.FindAsync(d => d.CouponCode == code && d.IsActive);
        
        if (discount == null)
            return Json(new { success = false, message = "Invalid coupon code." });
            
        // Check 1-time use per customer
        if (!string.IsNullOrWhiteSpace(phone))
        {
            var user = _userManager.Users.FirstOrDefault(u => u.PhoneNumber == phone || u.Email == $"{phone}@guest.local");
            if (user != null)
            {
                var alreadyUsed = await _unitOfWork.Orders.FindAsync(o => o.UserId == user.Id && o.CouponCode == code);
                if (alreadyUsed != null)
                    return Json(new { success = false, message = "Customer has already used this coupon." });
            }
        }
            
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
    // FIX #4: Soft-cancel instead of physical delete — preserves order history
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var order = await _unitOfWork.Orders.GetByIdAsync(id);
        if (order is null) return NotFound();

        // Return stock if order was not already cancelled
        if (order.Status != SD.Status_Cancelled)
            await ReturnStockAsync(order.Id);

        order.Status      = SD.Status_Cancelled;
        order.UpdatedAt   = DateTime.UtcNow;
        order.CancelledAt = DateTime.UtcNow;  // Start 24h UI countdown
        _unitOfWork.Orders.Update(order);
        await _unitOfWork.SaveAsync();

        TempData["success"] = $"Order #{id} has been cancelled. It will be hidden from this list in 24 hours.";
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

    private static bool IsValidOrderTransition(string from, string to)
    {
        if (from == to) return true;
        if (from == SD.Status_Delivered) return false;
        if (from == SD.Status_Cancelled) return false;
        if (to == SD.Status_Cancelled) return from != SD.Status_Delivered;
        var order = new[]
        {
            SD.Status_Pending, SD.Status_Confirmed, SD.Status_Processing,
            SD.Status_Shipped, SD.Status_Delivered
        };
        return Array.IndexOf(order, to) > Array.IndexOf(order, from);
    }

    private static bool IsValidPaymentTransition(string from, string to)
    {
        if (from == to) return true;
        if (to == SD.Payment_Refunded && from != SD.Payment_Paid) return false;
        if (to == SD.Payment_Pending && from == SD.Payment_Paid) return false;
        return true;
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

    // ──────────────────────────────────────────────────────────
    //  Payment Synchronization Logic
    // ──────────────────────────────────────────────────────────
    private async Task SyncPaymentRecordAsync(Order order)
    {
        var existingPayment = await _unitOfWork.Payments.FindAsync(p => p.OrderId == order.Id);

        if (existingPayment != null)
        {
            existingPayment.Status = order.PaymentStatus;
            existingPayment.Amount = order.TotalAmount; // Sync amount in case it changed via Edit
            _unitOfWork.Payments.Update(existingPayment);
        }
        else if (order.PaymentStatus != SD.Payment_Unpaid)
        {
            // Only create if we transitioned to a tracked state (Paid, Pending, Failed, Refunded)
            var newPayment = new Payment
            {
                OrderId = order.Id,
                Amount = order.TotalAmount,
                Provider = "Manual/System",
                TransactionId = "SYS-" + DateTime.UtcNow.Ticks,
                Status = order.PaymentStatus,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.Payments.AddAsync(newPayment);
        }
    }
}
