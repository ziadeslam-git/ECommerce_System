using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce_System.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SD.Role_Admin)]
public class PaymentsController : Controller
{
    private readonly IUnitOfWork _unitOfWork;
    private const int PageSize = 15;

    public PaymentsController(IUnitOfWork unitOfWork)
        => _unitOfWork = unitOfWork;

    // GET: /Admin/Payments
    //  FIX: server-side pagination so stats reflect ALL records, not just the current page
    public async Task<IActionResult> Index(
        int page = 1, string? statusFilter = null, string? searchQuery = null)
    {
        // ── 1. Global stats — NO filter, full table ──────────────────────
        //  These two queries go directly to DB (no in-memory loading)
        var allPayments = await _unitOfWork.Payments.GetAllAsync(tracked: false);
        var allList = allPayments.ToList();

        ViewBag.TotalVolume   = allList.Where(p => p.Status == SD.Payment_Paid).Sum(p => p.Amount);
        ViewBag.PaidCount     = allList.Count(p => p.Status == SD.Payment_Paid);
        ViewBag.PendingCount  = allList.Count(p => p.Status == SD.Payment_Pending);
        ViewBag.FailedCount   = allList.Count(p => p.Status == "Failed" || p.Status == "Refunded");

        // ── 2. Build filter expression ─────────────────────────────────
        System.Linq.Expressions.Expression<Func<Payment, bool>>? filter = null;

        if (!string.IsNullOrWhiteSpace(statusFilter) && !string.IsNullOrWhiteSpace(searchQuery))
        {
            var q = searchQuery.Trim();
            filter = p => p.Status == statusFilter &&
                          (p.Order.User!.Email!.Contains(q) || p.OrderId.ToString().Contains(q));
        }
        else if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            filter = p => p.Status == statusFilter;
        }
        else if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var q = searchQuery.Trim();
            filter = p => p.Order.User!.Email!.Contains(q) || p.OrderId.ToString().Contains(q);
        }

        // ── 3. Server-side paged query ─────────────────────────────────
        var (pagedItems, totalCount) = await _unitOfWork.Payments.GetPagedAsync(
            filter: filter,
            includeProperties: "Order,Order.User",
            page: page,
            pageSize: PageSize,
            tracked: false);

        // Note: GetPagedAsync on Payment doesn't order — we need IQueryable ordering.
        // Since Payment has simple fields, we order in memory after paging (acceptable for 15 items):
        var paged = pagedItems
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PaymentVM
            {
                Id            = p.Id,
                OrderId       = p.OrderId,
                CustomerName  = p.Order?.User?.FullName  ?? "Unknown",
                CustomerEmail = p.Order?.User?.Email     ?? string.Empty,
                Amount        = p.Amount,
                Provider      = p.Provider,
                TransactionId = p.TransactionId,
                Status        = p.Status,
                CreatedAt     = p.CreatedAt
            })
            .ToList();

        ViewBag.CurrentPage  = page;
        ViewBag.TotalPages   = (int)Math.Ceiling(totalCount / (double)PageSize);
        ViewBag.TotalCount   = totalCount;
        ViewBag.StatusFilter = statusFilter;
        ViewBag.SearchQuery  = searchQuery;
        ViewData["Title"]    = "Payments";
        return View(paged);
    }

    // GET: /Admin/Payments/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var payment = await _unitOfWork.Payments
            .FindAsync(p => p.Id == id, "Order,Order.User");

        if (payment == null) return NotFound();

        var vm = new PaymentVM
        {
            Id            = payment.Id,
            OrderId       = payment.OrderId,
            CustomerName  = payment.Order?.User?.FullName  ?? "Unknown",
            CustomerEmail = payment.Order?.User?.Email     ?? string.Empty,
            Amount        = payment.Amount,
            Provider      = payment.Provider,
            TransactionId = payment.TransactionId,
            Status        = payment.Status,
            CreatedAt     = payment.CreatedAt
        };

        ViewData["Title"] = "Payment Details";
        return View(vm);
    }

    // NO Create, Edit, Delete — Payments are read-only transaction records
}
