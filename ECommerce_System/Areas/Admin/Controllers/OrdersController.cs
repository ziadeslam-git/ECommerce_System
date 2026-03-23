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
        if (!ModelState.IsValid)
        {
            await PopulateCreateDropdownsAsync();
            return View(vm);
        }

        // Verify address belongs to the chosen user
        var address = await _unitOfWork.Addresses.GetByIdAsync(vm.AddressId);
        if (address is null || address.UserId != vm.UserId)
        {
            ModelState.AddModelError("", "Invalid address selected.");
            await PopulateCreateDropdownsAsync();
            return View(vm);
        }

        var order = new Order
        {
            UserId        = vm.UserId,
            AddressId     = vm.AddressId,
            Status        = vm.Status,
            PaymentStatus = vm.PaymentStatus,
            Subtotal      = vm.Subtotal,
            DiscountAmount= vm.DiscountAmount,
            TotalAmount   = vm.Subtotal - vm.DiscountAmount,
            CouponCode    = vm.CouponCode,
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow
        };

        await _unitOfWork.Orders.AddAsync(order);
        await _unitOfWork.SaveAsync();

        TempData["Success"] = $"Order #{order.Id} created successfully.";
        return RedirectToAction(nameof(Details), new { id = order.Id });
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
        var users = _userManager.Users.ToList();
        ViewBag.Users = users
            .Select(u => new SelectListItem { Value = u.Id, Text = $"{u.FullName} ({u.Email})" })
            .ToList();

        var addresses = await _unitOfWork.Addresses.GetAllAsync();
        ViewBag.Addresses = addresses
            .Select(a => new SelectListItem
            {
                Value = a.Id.ToString(),
                Text  = $"{a.FullName} – {a.Street}, {a.City}"
            })
            .ToList();

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
            var variant = await _unitOfWork.ProductVariants.GetByIdAsync(item.ProductVariantId);
            if (variant is null) continue;
            variant.Stock += item.Quantity;
            _unitOfWork.ProductVariants.Update(variant);
        }
    }
}
