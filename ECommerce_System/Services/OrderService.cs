using ECommerce_System.Common;
using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Services.Interfaces;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Admin;
using ECommerce_System.ViewModels.Customer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using static ECommerce_System.Utilities.OrderEmailTemplateBuilder;

namespace ECommerce_System.Services;

/// <summary>
/// Handles order management for both Admin and Customer areas.
/// Extracts duplicated logic from:
///   - Admin/OrdersController:    ReturnStockAsync, IsValidOrderTransition, UpdateStatus
///   - Customer/OrdersController: Cancel, Index (customer), ReturnStockAsync
///
/// Key responsibilities:
///   - Order status transitions with validation
///   - Stock restoration on cancellation
///   - Status change email notifications
///   - Paged order listing for admin and customer
/// </summary>
public sealed class OrderService(
    IUnitOfWork                         uow,
    UserManager<ApplicationUser>        userManager,
    IEmailSender                        emailSender,
    ILogger<OrderService>               logger,
    IConfiguration                      configuration)
    : IOrderService
{
    // ── Admin ─────────────────────────────────────────────────────────────────

    public async Task<(IList<OrderIndexVM> Orders, int TotalCount, int TotalPages)> GetAdminOrdersAsync(
        string? status, int page, int pageSize)
    {
        page = Math.Max(1, page);
        var cutoff = DateTime.UtcNow.AddHours(-24);

        var query = uow.Orders.Query()
            .AsNoTracking()
            .Where(o => o.Status != SD.Status_Cancelled
                     || !o.CancelledAt.HasValue
                     || o.CancelledAt.Value > cutoff);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(o => o.Status.Equals(status));

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        if (page > totalPages) page = totalPages;

        var orders = await query
            .Include(o => o.User)
            .Include(o => o.Address)
            .Include(o => o.OrderItems)
            .Include(o => o.Shipment)
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var vms = orders.Select(o => new OrderIndexVM
        {
            Id             = o.Id,
            CustomerName   = o.User?.FullName  ?? o.Address?.FullName ?? "Unknown",
            CustomerEmail  = o.User?.Email     ?? string.Empty,
            ItemCount      = o.OrderItems?.Count ?? 0,
            TotalAmount    = o.TotalAmount,
            Status         = o.Status,
            PaymentStatus  = o.PaymentStatus,
            CreatedAt      = o.CreatedAt,
            ShipmentId     = o.Shipment?.Id,
            ShipmentStatus = o.Shipment?.Status
        }).ToList();

        return (vms, totalCount, totalPages);
    }

    public async Task<Result<bool>> UpdateOrderStatusAsync(UpdateOrderStatusVM vm)
    {
        var order = await uow.Orders.GetByIdAsync(vm.OrderId);
        if (order is null)
            return Result<bool>.Failure($"Order #{vm.OrderId} not found.");

        if (!IsValidOrderTransition(vm.CurrentStatus, vm.NewStatus))
            return Result<bool>.Failure($"Cannot transition order from '{vm.CurrentStatus}' to '{vm.NewStatus}'.");

        if (!IsValidPaymentTransition(vm.CurrentPaymentStatus, vm.NewPaymentStatus))
            return Result<bool>.Failure("Invalid payment status transition.");

        var previousOrderStatus   = order.Status;
        var previousPaymentStatus = order.PaymentStatus;

        order.Status        = vm.NewStatus;
        order.PaymentStatus = vm.NewPaymentStatus;
        order.UpdatedAt     = DateTime.UtcNow;

        // BR-009: return stock automatically on cancellation
        if (vm.NewStatus == SD.Status_Cancelled && vm.CurrentStatus != SD.Status_Cancelled)
        {
            await ReturnStockAsync(order.Id);
            order.CancelledAt = DateTime.UtcNow;
        }

        await SyncPaymentRecordAsync(order);
        uow.Orders.Update(order);
        await uow.SaveAsync();

        // Send email if status changed
        var orderStatusChanged   = !previousOrderStatus.Equals(order.Status, StringComparison.OrdinalIgnoreCase);
        var paymentStatusChanged = !previousPaymentStatus.Equals(order.PaymentStatus, StringComparison.OrdinalIgnoreCase);

        if (orderStatusChanged || paymentStatusChanged)
        {
            _ = Task.Run(() => SendOrderStatusEmailAsync(
                order, previousOrderStatus, previousPaymentStatus, configuration["App:PublicBaseUrl"]));
        }

        logger.LogInformation(
            "Order #{OrderId} status updated: {OldStatus} → {NewStatus}, Payment: {OldPayment} → {NewPayment}.",
            order.Id, previousOrderStatus, order.Status, previousPaymentStatus, order.PaymentStatus);

        return Result<bool>.Success(true);
    }

    // ── Customer ──────────────────────────────────────────────────────────────

    public async Task<(IList<OrderIndexCustomerVM> Orders, int TotalCount)> GetCustomerOrdersAsync(
        string userId, int page, int pageSize)
    {
        var (orders, totalCount) = await uow.Orders.GetOrdersByUserPagedAsync(userId, page, pageSize);

        var vms = orders.Select(o =>
        {
            var firstItem   = o.OrderItems.FirstOrDefault();
            var mainImgUrl  = firstItem?.ProductVariant?.Product?.Images
                                         .FirstOrDefault(i => i.IsMain)?.ImageUrl
                           ?? firstItem?.ProductVariant?.Product?.Images
                                         .OrderBy(i => i.DisplayOrder)
                                         .FirstOrDefault()?.ImageUrl;

            return new OrderIndexCustomerVM
            {
                Id            = o.Id,
                CreatedAt     = o.CreatedAt,
                Status        = o.Status,
                PaymentStatus = o.PaymentStatus,
                TotalAmount   = o.TotalAmount,
                ItemCount     = o.OrderItems.Sum(oi => oi.Quantity),
                MainImageUrl  = mainImgUrl
            };
        }).ToList();

        return (vms, totalCount);
    }

    // ── Shared ────────────────────────────────────────────────────────────────

    public async Task<Result<bool>> CancelOrderAsync(int orderId, string userId, bool isAdmin = false)
    {
        var order = await uow.Orders.FindAsync(
            o => o.Id == orderId,
            includeProperties: "OrderItems,OrderItems.ProductVariant");

        if (order is null)
            return Result<bool>.Failure($"Order #{orderId} not found.");

        // IDOR protection: only the owner (or admin) can cancel
        if (!isAdmin && order.UserId != userId)
        {
            logger.LogWarning(
                "Cancel blocked: UserId={UserId} tried to cancel OrderId={OrderId} owned by {Owner}.",
                userId, orderId, order.UserId);
            return Result<bool>.Failure("You are not authorized to cancel this order.");
        }

        // Business rule: only Pending and Confirmed can be cancelled by customers
        if (!isAdmin && order.Status != SD.Status_Pending && order.Status != SD.Status_Confirmed)
            return Result<bool>.Failure($"Order #{orderId} cannot be cancelled at status '{order.Status}'.");

        using var transaction = await uow.BeginTransactionAsync();
        try
        {
            await ReturnStockAsync(order.Id);

            order.Status      = SD.Status_Cancelled;
            order.CancelledAt = DateTime.UtcNow;
            order.UpdatedAt   = DateTime.UtcNow;
            uow.Orders.Update(order);

            await uow.SaveAsync();
            await transaction.CommitAsync();

            logger.LogInformation("Order #{OrderId} cancelled by {UserId} (isAdmin={IsAdmin}).", orderId, userId, isAdmin);
            return Result<bool>.Success(true);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync();
            logger.LogWarning(ex, "Concurrency conflict cancelling Order #{OrderId}.", orderId);
            return Result<bool>.Failure("A conflict occurred while cancelling the order. Please try again.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Failed to cancel Order #{OrderId}.", orderId);
            throw;
        }
    }

    public bool IsValidOrderTransition(string from, string to)
    {
        if (from == to)                     return true;
        if (from == SD.Status_Delivered)    return false;
        if (from == SD.Status_Cancelled)    return false;
        if (to   == SD.Status_Cancelled)    return from != SD.Status_Delivered;

        var chain = new[]
        {
            SD.Status_Pending, SD.Status_Confirmed, SD.Status_Processing,
            SD.Status_Shipped, SD.Status_Delivered
        };
        return Array.IndexOf(chain, to) > Array.IndexOf(chain, from);
    }

    public bool IsValidPaymentTransition(string from, string to)
    {
        if (from == to)                                                return true;
        if (to == SD.Payment_Refunded && from != SD.Payment_Paid)    return false;
        if (to == SD.Payment_Pending  && from == SD.Payment_Paid)    return false;
        return true;
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Restores stock for all items in an order and re-activates variants that had been
    /// deactivated due to zero stock. Single query — no N+1.
    /// </summary>
    internal async Task ReturnStockAsync(int orderId)
    {
        var items = await uow.OrderItems
            .Query()
            .IgnoreQueryFilters()
            .Where(i => i.OrderId == orderId)
            .Include(i => i.ProductVariant)
            .ToListAsync();

        foreach (var item in items)
        {
            var variant = item.ProductVariant;
            if (variant is null)
            {
                logger.LogWarning(
                    "ReturnStock: OrderItem {ItemId} in Order {OrderId} has no variant. Skipping.",
                    item.Id, orderId);
                continue;
            }

            variant.Stock += item.Quantity;

            if (!variant.IsActive && variant.Stock > 0)
            {
                variant.IsActive = true;
                logger.LogInformation(
                    "ReturnStock: Variant {VariantId} re-activated (stock restored). OrderId={OrderId}.",
                    variant.Id, orderId);
            }
        }
    }

    private async Task SyncPaymentRecordAsync(Order order)
    {
        var existing = await uow.Payments.FindAsync(p => p.OrderId == order.Id);

        if (existing is not null)
        {
            existing.Status = order.PaymentStatus;
            existing.Amount = order.TotalAmount;
            uow.Payments.Update(existing);
        }
        else if (order.PaymentStatus != SD.Payment_Unpaid)
        {
            var payment = new Payment
            {
                OrderId       = order.Id,
                Amount        = order.TotalAmount,
                Provider      = "Manual/System",
                TransactionId = $"SYS-{DateTime.UtcNow.Ticks}",
                Status        = order.PaymentStatus,
                CreatedAt     = DateTime.UtcNow
            };
            await uow.Payments.AddAsync(payment);
        }
    }

    private async Task SendOrderStatusEmailAsync(
        Order  order,
        string previousOrderStatus,
        string previousPaymentStatus,
        string? publicBaseUrl)
    {
        try
        {
            var user = await userManager.FindByIdAsync(order.UserId);
            if (string.IsNullOrWhiteSpace(user?.Email)) return;

            var baseUrl      = (publicBaseUrl ?? string.Empty).TrimEnd('/');
            var detailsPath  = $"/Customer/Orders/Details/{order.Id}";
            var detailsUrl   = $"{baseUrl}{detailsPath}";

            var emailContent = OrderEmailTemplateBuilder.BuildStatusUpdateEmail(
                user.FullName,
                order.Id,
                order.TotalAmount,
                previousOrderStatus,
                order.Status,
                previousPaymentStatus,
                order.PaymentStatus,
                detailsUrl);

            await emailSender.SendEmailAsync(user.Email, emailContent.Subject, emailContent.HtmlBody);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send status update email for Order #{OrderId}.", order.Id);
        }
    }
}
