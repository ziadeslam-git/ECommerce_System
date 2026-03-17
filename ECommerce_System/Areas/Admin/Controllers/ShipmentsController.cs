using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce_System.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SD.Role_Admin)]
public class ShipmentsController : Controller
{
    private readonly IUnitOfWork _unitOfWork;

    public ShipmentsController(IUnitOfWork unitOfWork)
        => _unitOfWork = unitOfWork;

    // ──────────────────────────────────────────────────────────
    //  GET /Admin/Shipments/Create?orderId=5
    // ──────────────────────────────────────────────────────────
    public async Task<IActionResult> Create(int orderId)
    {
        var order = await _unitOfWork.Orders.GetByIdAsync(orderId);
        if (order is null) return NotFound();

        // Only one shipment per order
        var existing = await _unitOfWork.Shipments
            .FindAsync(s => s.OrderId == orderId);

        if (existing is not null)
        {
            TempData["Warning"] = "A shipment already exists for this order. You can edit it instead.";
            return RedirectToAction(nameof(Edit), new { id = existing.Id });
        }

        var vm = new ShipmentVM
        {
            OrderId = orderId,
            Status  = SD.Shipment_Pending
        };
        return View(vm);
    }

    // ──────────────────────────────────────────────────────────
    //  POST /Admin/Shipments/Create
    // ──────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ShipmentVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var shipment = new Shipment
        {
            OrderId           = vm.OrderId,
            TrackingNumber    = vm.TrackingNumber,
            Carrier           = vm.Carrier,
            Status            = vm.Status,
            EstimatedDelivery = vm.EstimatedDelivery,
            ShippedAt         = vm.Status == SD.Shipment_Shipped ? DateTime.UtcNow : null,
            DeliveredAt       = vm.Status == SD.Shipment_Delivered ? DateTime.UtcNow : null
        };

        await _unitOfWork.Shipments.AddAsync(shipment);

        // Sync order status if shipped
        if (vm.Status == SD.Shipment_Shipped || vm.Status == SD.Shipment_OutForDelivery)
        {
            var order = await _unitOfWork.Orders.GetByIdAsync(vm.OrderId);
            if (order is not null)
            {
                order.Status    = SD.Status_Shipped;
                order.UpdatedAt = DateTime.UtcNow;
                _unitOfWork.Orders.Update(order);
            }
        }

        await _unitOfWork.SaveAsync();

        TempData["Success"] = "Shipment created successfully.";
        return RedirectToAction("Details", "Orders", new { id = vm.OrderId });
    }

    // ──────────────────────────────────────────────────────────
    //  GET /Admin/Shipments/Edit/{id}
    // ──────────────────────────────────────────────────────────
    public async Task<IActionResult> Edit(int id)
    {
        var shipment = await _unitOfWork.Shipments.GetByIdAsync(id);
        if (shipment is null) return NotFound();

        var vm = new ShipmentVM
        {
            Id                = shipment.Id,
            OrderId           = shipment.OrderId,
            TrackingNumber    = shipment.TrackingNumber,
            Carrier           = shipment.Carrier,
            Status            = shipment.Status,
            EstimatedDelivery = shipment.EstimatedDelivery,
            ShippedAt         = shipment.ShippedAt,
            DeliveredAt       = shipment.DeliveredAt
        };
        return View(vm);
    }

    // ──────────────────────────────────────────────────────────
    //  POST /Admin/Shipments/Edit
    // ──────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ShipmentVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var shipment = await _unitOfWork.Shipments.GetByIdAsync(vm.Id);
        if (shipment is null) return NotFound();

        // Track shipped/delivered timestamps
        if (vm.Status == SD.Shipment_Shipped && shipment.ShippedAt is null)
            shipment.ShippedAt = DateTime.UtcNow;

        if (vm.Status == SD.Shipment_Delivered && shipment.DeliveredAt is null)
            shipment.DeliveredAt = DateTime.UtcNow;

        shipment.TrackingNumber    = vm.TrackingNumber;
        shipment.Carrier           = vm.Carrier;
        shipment.Status            = vm.Status;
        shipment.EstimatedDelivery = vm.EstimatedDelivery;

        _unitOfWork.Shipments.Update(shipment);

        // Sync order status with shipment status
        var order = await _unitOfWork.Orders.GetByIdAsync(shipment.OrderId);
        if (order is not null)
        {
            order.Status = vm.Status switch
            {
                SD.Shipment_Shipped        => SD.Status_Shipped,
                SD.Shipment_OutForDelivery => SD.Status_Shipped,
                SD.Shipment_Delivered      => SD.Status_Delivered,
                _                          => order.Status
            };
            order.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.Orders.Update(order);
        }

        await _unitOfWork.SaveAsync();

        TempData["Success"] = "Shipment updated successfully.";
        return RedirectToAction("Details", "Orders", new { id = shipment.OrderId });
    }
}
