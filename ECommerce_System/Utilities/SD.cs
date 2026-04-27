namespace ECommerce_System.Utilities;

public static class SD
{
    // ──────────────── Roles ────────────────
    public const string Role_Admin    = "Admin";
    public const string Role_Customer = "Customer";

    // ──────────────── Order Status ─────────────────
    public const string Status_Pending    = "Pending";
    public const string Status_Confirmed  = "Confirmed";
    public const string Status_Processing = "Processing";
    public const string Status_Shipped    = "Shipped";
    public const string Status_Delivered  = "Delivered";
    public const string Status_Cancelled  = "Cancelled";

    // ──────────────── Payment Status ───────────────
    public const string Payment_Unpaid   = "Unpaid";
    public const string Payment_Paid     = "Paid";
    public const string Payment_Refunded = "Refunded";
    public const string Payment_Failed   = "Failed";
    public const string Payment_Pending  = "Pending";

    // ──────────────── Shipment Status ──────────────
    public const string Shipment_Pending         = "Pending";
    public const string Shipment_Shipped         = "Shipped";
    public const string Shipment_OutForDelivery  = "OutForDelivery";
    public const string Shipment_Delivered       = "Delivered";

    // ──────────────── Discount Type ────────────────
    public const string Discount_Percentage   = "Percentage";
    public const string Discount_FixedAmount  = "FixedAmount";

    // ──────────────── Image Upload ─────────────────
    public const string Cloudinary_ProductFolder = "ecommerce/products";
    public const string Cloudinary_ProfileFolder = "ecommerce/profiles";

    // ──────────────── Default Admin ────────────────
    public const string Admin_Email    = "admin@ecommerce.com";
    public const string Admin_Password = "Admin@123456";
    public const string Admin_FullName = "System Administrator";
}
