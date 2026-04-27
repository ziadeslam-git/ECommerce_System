using ECommerce_System.Models;
using ECommerce_System.ViewModels.Admin;

namespace ECommerce_System.ViewModels.Customer;

// ─── Checkout ────────────────────────────────────────────────────────────────
public class CheckoutVM
{
    public List<CheckoutCartItemVM> Items          { get; set; } = [];
    public decimal          Subtotal       { get; set; }
    public IList<Address>   Addresses      { get; set; } = [];
    public int?             DefaultAddressId { get; set; }

    // Re-populate on validation failure
    public string?  CouponCode  { get; set; }
    public int?     AddressId   { get; set; }
}

// ─── Cart item shown on checkout page ────────────────────────────────────────
public class CheckoutCartItemVM
{
    public int     Id               { get; set; }
    public int     CartId           { get; set; }
    public int     ProductVariantId { get; set; }
    public int     Quantity         { get; set; }
    public decimal PriceSnapshot    { get; set; }

    /// <summary>Live price from DB — used for subtotal on checkout page.</summary>
    public decimal CurrentPrice     { get; set; }

    public string  ProductName { get; set; } = string.Empty;
    public string  Size        { get; set; } = string.Empty;
    public string  Color       { get; set; } = string.Empty;
    public string? MainImageUrl { get; set; }
    public int     Stock       { get; set; }

    public decimal LineTotal   => Quantity * CurrentPrice;
}

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

    public ShipmentVM?         Shipment { get; set; }
    public List<OrderItemVM>   Items    { get; set; } = [];

    public bool CanCancel => Status == Utilities.SD.Status_Pending;
    public bool CanReview => Status == Utilities.SD.Status_Delivered;
}
