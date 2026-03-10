namespace ECommerce_System.Models;

public class Shipment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }
    public string Status { get; set; } = "Pending";    // SD.Shipment_*
    public DateOnly? EstimatedDelivery { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }

    // Navigation
    public Order Order { get; set; } = null!;
}
