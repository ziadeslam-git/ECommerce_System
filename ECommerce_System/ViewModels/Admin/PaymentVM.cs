namespace ECommerce_System.ViewModels.Admin;

public class PaymentVM
{
    public int Id { get; set; }
    public int OrderId { get; set; }

    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Provider { get; set; } = string.Empty;  // 'Stripe'
    public string? TransactionId { get; set; }
    public string Status { get; set; } = string.Empty;    // Pending/Paid/Failed/Refunded

    public DateTime CreatedAt { get; set; }

    // For UI
    public string StatusBadgeColor => Status switch
    {
        "Paid"     => "emerald",
        "Failed"   => "rose",
        "Refunded" => "amber",
        _          => "slate"
    };
}
