namespace ECommerce_System.ViewModels.Customer;

public class CheckoutVM
{
    public List<CheckoutItemCustomerVM> Items { get; set; } = [];
    public List<AddressOptionCustomerVM> Addresses { get; set; } = [];
    public int? DefaultAddressId { get; set; }
    public string? CouponCode { get; set; }
    public string PaymentMethod { get; set; } = "CashOnDelivery";
    public bool CouponApplied { get; set; }
    public string? CouponMessage { get; set; }
    public int? AddressId { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }

    public decimal Total => Subtotal - DiscountAmount;
    public int ItemsCount => Items.Sum(i => i.Quantity);
}

public class CheckoutItemCustomerVM
{
    public int CartItemId { get; set; }
    public int ProductVariantId { get; set; }
    public int Quantity { get; set; }
    public decimal PriceSnapshot { get; set; }
    public decimal CurrentPrice { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int Stock { get; set; }

    public decimal DisplayPrice => CurrentPrice > 0 ? CurrentPrice : PriceSnapshot;
    public decimal LineTotal => Quantity * DisplayPrice;
}

public class AddressOptionCustomerVM
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string Country { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public string DisplayLine { get; set; } = string.Empty;
}

public class WishlistIndexVM
{
    public List<WishlistItemCustomerVM> Items { get; set; } = [];

    public bool IsEmpty => Items.Count == 0;
}

public class WishlistItemCustomerVM
{
    public int WishlistItemId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public string? ImageUrl { get; set; }
    public double AverageRating { get; set; }
}
