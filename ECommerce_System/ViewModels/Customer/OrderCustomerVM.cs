namespace ECommerce_System.ViewModels.Customer;

// ─── My Orders list ──────────────────────────────────────────────────────────
public class OrderIndexCustomerVM
{
    public int      Id            { get; set; }
    public DateTime CreatedAt     { get; set; }
    public string   Status        { get; set; } = string.Empty;
    public string   PaymentStatus { get; set; } = string.Empty;
    public decimal  TotalAmount   { get; set; }
    public int      ItemCount     { get; set; }

    /// <summary>Main image of the first OrderItem's product.</summary>
    public string? MainImageUrl { get; set; }
}

// ─── Pagination wrapper for Orders/Index ─────────────────────────────────────
public class OrderIndexPagedVM
{
    public IReadOnlyList<OrderIndexCustomerVM> Orders      { get; set; } = [];
    public int                                 CurrentPage { get; set; }
    public int                                 TotalPages  { get; set; }
    public int                                 TotalCount  { get; set; }

    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage     => CurrentPage < TotalPages;
}

// ─── Order Details ────────────────────────────────────────────────────────────
public class OrderDetailsCustomerVM
{
    public int      Id             { get; set; }
    public DateTime CreatedAt      { get; set; }
    public string   Status         { get; set; } = string.Empty;
    public string   PaymentStatus  { get; set; } = string.Empty;
    public decimal  Subtotal       { get; set; }
    public decimal  DiscountAmount { get; set; }
    public decimal  TotalAmount    { get; set; }
    public string?  CouponCode     { get; set; }
    public string   AddressLine    { get; set; } = string.Empty;

    public ShipmentSummaryCustomerVM? Shipment { get; set; }
    public List<OrderItemCustomerVM> Items { get; set; } = [];

    public bool CanCancel => Status == Utilities.SD.Status_Pending;
    public bool CanReview => Status == Utilities.SD.Status_Delivered;
}

public class OrderItemCustomerVM
{
    public string ProductName { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
}

public class ShipmentSummaryCustomerVM
{
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateOnly? EstimatedDelivery { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}
