using System.Globalization;
using System.Net;

namespace ECommerce_System.Utilities;

public sealed record OrderEmailContent(string Subject, string HtmlBody);

public static class OrderEmailTemplateBuilder
{
    public static OrderEmailContent BuildInitialOrderEmail(
        string customerName,
        int orderId,
        decimal totalAmount,
        string orderStatus,
        string paymentStatus,
        string orderDetailsUrl)
    {
        var headline = paymentStatus == SD.Payment_Paid
            ? "Payment Confirmed"
            : "Order Received";

        var subject = paymentStatus == SD.Payment_Paid
            ? $"Smart Store — Payment Received for Order #{orderId}"
            : $"Smart Store — Order #{orderId} Received";

        var summary = paymentStatus == SD.Payment_Paid
            ? "We've received your payment successfully. Your order is now in our review queue and will move to confirmation shortly."
            : "We've received your order successfully. Our team will review it and confirm the next step shortly.";

        var changes = new[]
        {
            $"Order status: {ToDisplayOrderStatus(orderStatus)}",
            $"Payment status: {ToDisplayPaymentStatus(paymentStatus)}"
        };

        var htmlBody = BuildEmailHtml(
            customerName,
            orderId,
            totalAmount,
            headline,
            summary,
            orderStatus,
            paymentStatus,
            changes,
            orderDetailsUrl,
            "View My Order");

        return new OrderEmailContent(subject, htmlBody);
    }

    public static OrderEmailContent BuildStatusUpdateEmail(
        string customerName,
        int orderId,
        decimal totalAmount,
        string previousOrderStatus,
        string currentOrderStatus,
        string previousPaymentStatus,
        string currentPaymentStatus,
        string orderDetailsUrl)
    {
        var orderChanged = !string.Equals(previousOrderStatus, currentOrderStatus, StringComparison.OrdinalIgnoreCase);
        var paymentChanged = !string.Equals(previousPaymentStatus, currentPaymentStatus, StringComparison.OrdinalIgnoreCase);

        var headline = ResolveHeadline(currentOrderStatus, currentPaymentStatus, orderChanged);
        var subject = $"Smart Store — {headline} for Order #{orderId}";
        var summary = BuildUpdateSummary(previousOrderStatus, currentOrderStatus, previousPaymentStatus, currentPaymentStatus, orderChanged, paymentChanged);

        var changes = new List<string>();
        if (orderChanged)
        {
            changes.Add($"Order status changed from {ToDisplayOrderStatus(previousOrderStatus)} to {ToDisplayOrderStatus(currentOrderStatus)}");
        }

        if (paymentChanged)
        {
            changes.Add($"Payment status changed from {ToDisplayPaymentStatus(previousPaymentStatus)} to {ToDisplayPaymentStatus(currentPaymentStatus)}");
        }

        var htmlBody = BuildEmailHtml(
            customerName,
            orderId,
            totalAmount,
            headline,
            summary,
            currentOrderStatus,
            currentPaymentStatus,
            changes,
            orderDetailsUrl,
            "Track This Order");

        return new OrderEmailContent(subject, htmlBody);
    }

    public static string ToDisplayOrderStatus(string status) => status switch
    {
        SD.Status_Pending => "Pending",
        SD.Status_Confirmed => "Confirmed",
        SD.Status_Processing => "Processing",
        SD.Status_Shipped => "Shipped",
        SD.Status_Delivered => "Delivered",
        SD.Status_Cancelled => "Cancelled",
        _ => string.IsNullOrWhiteSpace(status) ? "Unknown" : status
    };

    public static string ToDisplayPaymentStatus(string status) => status switch
    {
        SD.Payment_Unpaid => "Unpaid",
        SD.Payment_Pending => "Pending",
        SD.Payment_Paid => "Paid",
        SD.Payment_Refunded => "Refunded",
        SD.Payment_Failed => "Failed",
        _ => string.IsNullOrWhiteSpace(status) ? "Unknown" : status
    };

    private static string ResolveHeadline(string currentOrderStatus, string currentPaymentStatus, bool orderChanged)
    {
        if (orderChanged)
        {
            return currentOrderStatus switch
            {
                SD.Status_Confirmed => "Order Confirmed",
                SD.Status_Processing => "Order in Preparation",
                SD.Status_Shipped => "Order Shipped",
                SD.Status_Delivered => "Order Delivered",
                SD.Status_Cancelled => "Order Cancelled",
                _ => "Order Updated"
            };
        }

        return currentPaymentStatus switch
        {
            SD.Payment_Paid => "Payment Confirmed",
            SD.Payment_Refunded => "Refund Recorded",
            SD.Payment_Failed => "Payment Update",
            SD.Payment_Pending => "Payment Pending",
            _ => "Order Updated"
        };
    }

    private static string BuildUpdateSummary(
        string previousOrderStatus,
        string currentOrderStatus,
        string previousPaymentStatus,
        string currentPaymentStatus,
        bool orderChanged,
        bool paymentChanged)
    {
        if (orderChanged)
        {
            return currentOrderStatus switch
            {
                SD.Status_Confirmed => "Your order has been confirmed and is moving into preparation.",
                SD.Status_Processing => "Your order is now being prepared by the store team.",
                SD.Status_Shipped => "Your order is now on the way. Keep an eye on your delivery progress.",
                SD.Status_Delivered => "Your order has been marked as delivered. We hope you enjoy it.",
                SD.Status_Cancelled => "Your order has been cancelled and will no longer continue through fulfillment.",
                _ => $"Your order has been updated from {ToDisplayOrderStatus(previousOrderStatus)} to {ToDisplayOrderStatus(currentOrderStatus)}."
            };
        }

        if (paymentChanged)
        {
            return currentPaymentStatus switch
            {
                SD.Payment_Paid => "We've recorded your payment successfully.",
                SD.Payment_Refunded => "Your payment has been marked as refunded in our system.",
                SD.Payment_Failed => "There was a payment update on your order. Please review the latest status.",
                SD.Payment_Pending => "Your payment is still pending confirmation.",
                _ => $"Your payment status has been updated from {ToDisplayPaymentStatus(previousPaymentStatus)} to {ToDisplayPaymentStatus(currentPaymentStatus)}."
            };
        }

        return "Your order details have been refreshed in our system.";
    }

    private static string BuildEmailHtml(
        string customerName,
        int orderId,
        decimal totalAmount,
        string headline,
        string summary,
        string orderStatus,
        string paymentStatus,
        IEnumerable<string> changes,
        string orderDetailsUrl,
        string ctaLabel)
    {
        var safeCustomerName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(customerName) ? "Customer" : customerName);
        var safeHeadline = WebUtility.HtmlEncode(headline);
        var safeSummary = WebUtility.HtmlEncode(summary);
        var safeOrderStatus = WebUtility.HtmlEncode(ToDisplayOrderStatus(orderStatus));
        var safePaymentStatus = WebUtility.HtmlEncode(ToDisplayPaymentStatus(paymentStatus));
        var safeTotalAmount = WebUtility.HtmlEncode(totalAmount.ToString("N2", CultureInfo.InvariantCulture) + " EGP");
        var safeOrderDetailsUrl = WebUtility.HtmlEncode(orderDetailsUrl);
        var safeCtaLabel = WebUtility.HtmlEncode(ctaLabel);

        var changeItems = changes
            .Where(change => !string.IsNullOrWhiteSpace(change))
            .Select(change => $"<li style=\"margin:0 0 8px; color:#475569; font-size:14px; line-height:1.7;\">{WebUtility.HtmlEncode(change)}</li>")
            .ToList();

        var changesSection = changeItems.Count == 0
            ? string.Empty
            : $"""
                <tr>
                    <td style="padding:0 40px 24px;">
                        <div style="background:#f8fafc; border:1px solid #e2e8f0; border-radius:16px; padding:20px 22px;">
                            <p style="margin:0 0 12px; color:#0f172a; font-size:14px; font-weight:700;">Latest changes</p>
                            <ul style="margin:0; padding-left:20px;">
                                {string.Join(Environment.NewLine, changeItems)}
                            </ul>
                        </div>
                    </td>
                </tr>
                """;

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>{{safeHeadline}}</title>
            </head>
            <body style="margin:0; padding:0; background-color:#f4f4f7; font-family:'Segoe UI', Arial, sans-serif;">
                <table width="100%" cellpadding="0" cellspacing="0" style="padding:40px 0;">
                    <tr>
                        <td align="center">
                            <table width="560" cellpadding="0" cellspacing="0" style="background:#ffffff; border-radius:16px; overflow:hidden; box-shadow:0 4px 24px rgba(0,0,0,0.08);">
                                <tr>
                                    <td style="background:linear-gradient(135deg, #6366f1 0%, #4f46e5 100%); padding:32px 40px; text-align:center;">
                                        <h1 style="margin:0; color:#ffffff; font-size:26px; font-weight:700; letter-spacing:-0.5px;">Smart Store</h1>
                                        <p style="margin:8px 0 0; color:rgba(255,255,255,0.82); font-size:14px;">Order updates delivered clearly.</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td style="padding:28px 40px 0;">
                                        <p style="margin:0; color:#6366f1; font-size:13px; font-weight:700; letter-spacing:0.12em; text-transform:uppercase;">Order #{{orderId}}</p>
                                        <h2 style="margin:12px 0 10px; color:#111827; font-size:28px; font-weight:800; letter-spacing:-0.6px;">{{safeHeadline}}</h2>
                                        <p style="margin:0; color:#475569; font-size:15px; line-height:1.8;">Hello {{safeCustomerName}}, {{safeSummary}}</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td style="padding:24px 40px;">
                                        <table width="100%" cellpadding="0" cellspacing="0" style="background:#f8fafc; border:1px solid #e2e8f0; border-radius:18px;">
                                            <tr>
                                                <td style="padding:20px 22px 8px;">
                                                    <p style="margin:0; color:#64748b; font-size:12px; font-weight:700; letter-spacing:0.08em; text-transform:uppercase;">Current status</p>
                                                </td>
                                            </tr>
                                            <tr>
                                                <td style="padding:0 22px 20px;">
                                                    <table width="100%" cellpadding="0" cellspacing="0">
                                                        <tr>
                                                            <td style="padding:0 0 14px;">
                                                                <span style="display:inline-block; padding:8px 14px; background:#ede9fe; color:#4f46e5; border-radius:999px; font-size:13px; font-weight:700;">Order: {{safeOrderStatus}}</span>
                                                            </td>
                                                        </tr>
                                                        <tr>
                                                            <td style="padding:0 0 18px;">
                                                                <span style="display:inline-block; padding:8px 14px; background:#ecfeff; color:#0f766e; border-radius:999px; font-size:13px; font-weight:700;">Payment: {{safePaymentStatus}}</span>
                                                            </td>
                                                        </tr>
                                                        <tr>
                                                            <td>
                                                                <p style="margin:0; color:#0f172a; font-size:14px; font-weight:700;">Total</p>
                                                                <p style="margin:6px 0 0; color:#111827; font-size:24px; font-weight:800;">{{safeTotalAmount}}</p>
                                                            </td>
                                                        </tr>
                                                    </table>
                                                </td>
                                            </tr>
                                        </table>
                                    </td>
                                </tr>
                                {{changesSection}}
                                <tr>
                                    <td style="padding:0 40px 32px; text-align:center;">
                                        <a href="{{safeOrderDetailsUrl}}" style="display:inline-block; padding:14px 34px; background:linear-gradient(135deg, #6366f1 0%, #4f46e5 100%); color:#ffffff; text-decoration:none; border-radius:12px; font-size:15px; font-weight:700; letter-spacing:0.2px;">{{safeCtaLabel}}</a>
                                    </td>
                                </tr>
                                <tr>
                                    <td style="padding:0 40px;">
                                        <hr style="border:none; border-top:1px solid #e5e7eb; margin:0;" />
                                    </td>
                                </tr>
                                <tr>
                                    <td style="padding:20px 40px 28px;">
                                        <p style="margin:0; color:#94a3b8; font-size:12px; text-align:center; line-height:1.7;">
                                            This update was sent automatically after your order record changed in Smart Store.<br />
                                            If you need help, reply to this email and our team will guide you.
                                        </p>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>
                </table>
            </body>
            </html>
            """;
    }
}
