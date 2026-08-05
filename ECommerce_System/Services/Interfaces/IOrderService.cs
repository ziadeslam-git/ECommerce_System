using ECommerce_System.Common;
using ECommerce_System.Models;
using ECommerce_System.ViewModels.Admin;
using ECommerce_System.ViewModels.Customer;

namespace ECommerce_System.Services.Interfaces;

/// <summary>
/// Handles order management business logic for both Admin and Customer areas.
/// Replaces duplicated order logic spread across Admin/OrdersController and Customer/OrdersController.
/// </summary>
public interface IOrderService
{
    // ── Admin ─────────────────────────────────────────────────────────────────

    /// <summary>Gets paginated orders for admin list view with optional status filter.</summary>
    Task<(IList<OrderIndexVM> Orders, int TotalCount, int TotalPages)> GetAdminOrdersAsync(
        string? status, int page, int pageSize);

    /// <summary>Validates and applies an order status transition.</summary>
    Task<Result<bool>> UpdateOrderStatusAsync(UpdateOrderStatusVM vm);

    // ── Customer ──────────────────────────────────────────────────────────────

    /// <summary>Gets paginated orders for a specific customer.</summary>
    Task<(IList<OrderIndexCustomerVM> Orders, int TotalCount)> GetCustomerOrdersAsync(
        string userId, int page, int pageSize);

    // ── Shared ────────────────────────────────────────────────────────────────

    /// <summary>Cancels an order (validates ownership + status), restores stock.</summary>
    Task<Result<bool>> CancelOrderAsync(int orderId, string userId, bool isAdmin = false);

    /// <summary>
    /// Validates that an order status transition is legal.
    /// Enforces: Pending → Confirmed → Processing → Shipped → Delivered
    /// </summary>
    bool IsValidOrderTransition(string from, string to);

    /// <summary>Validates that a payment status transition is legal.</summary>
    bool IsValidPaymentTransition(string from, string to);
}
