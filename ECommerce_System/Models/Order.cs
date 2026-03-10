namespace ECommerce_System.Models;

public class Order
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int AddressId { get; set; }
    public string Status { get; set; } = "Pending";         // SD.Status_*
    public string PaymentStatus { get; set; } = "Unpaid";   // SD.Payment_*
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; } = 0;
    public decimal TotalAmount { get; set; }
    public string? CouponCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ApplicationUser User { get; set; } = null!;
    public Address Address { get; set; } = null!;
    public ICollection<OrderItem> OrderItems { get; set; } = [];
    public Payment? Payment { get; set; }
    public Shipment? Shipment { get; set; }
}
