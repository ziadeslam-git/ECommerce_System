using System.ComponentModel.DataAnnotations;
using ECommerce_System.Utilities;

namespace ECommerce_System.ViewModels.Admin;

// ────────────────────────────────────────────────────────────
//  ShipmentVM  – Create + Edit form for a shipment
// ────────────────────────────────────────────────────────────
public class ShipmentVM
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    [Display(Name = "Tracking Number")]
    [StringLength(100)]
    public string? TrackingNumber { get; set; }

    [StringLength(100)]
    public string? Carrier { get; set; }

    [Display(Name = "Shipment Status")]
    public string Status { get; set; } = SD.Shipment_Pending;

    [Display(Name = "Estimated Delivery")]
    [DataType(DataType.Date)]
    public DateOnly? EstimatedDelivery { get; set; }

    public DateTime? ShippedAt   { get; set; }
    public DateTime? DeliveredAt { get; set; }

    // For display inside OrderDetailsVM
    public string? CustomerName { get; set; }

    // Populated from SD to build status dropdown
    public static IReadOnlyList<string> ShipmentStatuses { get; } =
    [
        SD.Shipment_Pending,
        SD.Shipment_Shipped,
        SD.Shipment_OutForDelivery,
        SD.Shipment_Delivered
    ];
}
