using ECommerce_System.Utilities;

namespace ECommerce_System.ViewModels.Admin;

// ────────────────────────────────────────────────────────────
//  OrderIndexVM  – used by Orders/Index list
// ────────────────────────────────────────────────────────────
public class OrderIndexVM
{
    public int             Id             { get; set; }
    public string          CustomerName   { get; set; } = string.Empty;
    public string          CustomerEmail  { get; set; } = string.Empty;
    public int             ItemCount      { get; set; }
    public decimal         TotalAmount    { get; set; }
    public string          Status         { get; set; } = string.Empty;
    public string          PaymentStatus  { get; set; } = string.Empty;
    public DateTime        CreatedAt      { get; set; }
}

// ────────────────────────────────────────────────────────────
//  OrderItemVM  – one line inside an order
// ────────────────────────────────────────────────────────────
public class OrderItemVM
{
    public string ProductName { get; set; } = string.Empty;
    public string Size        { get; set; } = string.Empty;
    public string Color       { get; set; } = string.Empty;
    public int    Quantity    { get; set; }
    public decimal UnitPrice  { get; set; }
    public decimal Subtotal   { get; set; }
}

// ────────────────────────────────────────────────────────────
//  OrderDetailsVM  – full order details page
// ────────────────────────────────────────────────────────────
public class OrderDetailsVM
{
    public int      Id            { get; set; }
    public string   CustomerName  { get; set; } = string.Empty;
    public string   CustomerEmail { get; set; } = string.Empty;
    public string   Status        { get; set; } = string.Empty;
    public string   PaymentStatus { get; set; } = string.Empty;
    public decimal  Subtotal      { get; set; }
    public decimal  DiscountAmount{ get; set; }
    public decimal  TotalAmount   { get; set; }
    public string?  CouponCode    { get; set; }
    public DateTime CreatedAt     { get; set; }

    // Address Snapshot
    public string AddressLine { get; set; } = string.Empty;

    // Items
    public List<OrderItemVM> Items { get; set; } = [];

    // Shipment (nullable – may not exist yet)
    public ShipmentVM? Shipment { get; set; }
}

// ────────────────────────────────────────────────────────────
//  UpdateOrderStatusVM  – form to change order/payment status
// ────────────────────────────────────────────────────────────
public class UpdateOrderStatusVM
{
    public int    OrderId       { get; set; }
    public string CurrentStatus { get; set; } = string.Empty;
    public string NewStatus     { get; set; } = string.Empty;

    public string CurrentPaymentStatus { get; set; } = string.Empty;
    public string NewPaymentStatus     { get; set; } = string.Empty;

    // Populated from SD to build dropdowns
    public static IReadOnlyList<string> OrderStatuses { get; } =
    [
        SD.Status_Pending,
        SD.Status_Confirmed,
        SD.Status_Processing,
        SD.Status_Shipped,
        SD.Status_Delivered,
        SD.Status_Cancelled
    ];

    public static IReadOnlyList<string> PaymentStatuses { get; } =
    [
        SD.Payment_Unpaid,
        SD.Payment_Pending,
        SD.Payment_Paid,
        SD.Payment_Refunded,
        SD.Payment_Failed
    ];
}
