using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECommerce_System.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SD.Role_Admin)]
public class PaymentsController : Controller
{
    private readonly IUnitOfWork _unitOfWork;
    private const int PageSize = 10;

    public PaymentsController(IUnitOfWork unitOfWork)
        => _unitOfWork = unitOfWork;

    // GET: /Admin/Payments
    //  FIX: server-side pagination so stats reflect ALL records, not just the current page
    public async Task<IActionResult> Index(
        int page = 1, string? statusFilter = null, string? searchQuery = null)
    {
        page = Math.Max(page, 1);

        var paymentsBaseQuery = _unitOfWork.Payments.Query().AsNoTracking();

        ViewBag.TotalVolume = await paymentsBaseQuery
            .Where(p => p.Status == SD.Payment_Paid)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;
        ViewBag.PaidCount = await paymentsBaseQuery.CountAsync(p => p.Status == SD.Payment_Paid);
        ViewBag.PendingCount = await paymentsBaseQuery.CountAsync(p => p.Status == SD.Payment_Pending);
        ViewBag.FailedCount = await paymentsBaseQuery.CountAsync(p => p.Status == SD.Payment_Failed || p.Status == SD.Payment_Refunded);

        var filteredQuery = _unitOfWork.Payments.Query()
            .AsNoTracking()
            .Include(p => p.Order)
            .ThenInclude(o => o.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            filteredQuery = filteredQuery.Where(p => p.Status == statusFilter);
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var q = searchQuery.Trim();
            filteredQuery = filteredQuery.Where(p =>
                p.OrderId.ToString().Contains(q) ||
                (p.Order != null && p.Order.User != null && p.Order.User.Email != null && p.Order.User.Email.Contains(q)));
        }

        var totalCount = await filteredQuery.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        if (page > totalPages)
            page = totalPages;

        var pagedPayments = await filteredQuery
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var paged = pagedPayments
            .Select(p => new PaymentVM
            {
                Id            = p.Id,
                OrderId       = p.OrderId,
                CustomerName  = p.Order != null && p.Order.User != null ? p.Order.User.FullName : "Unknown",
                CustomerEmail = p.Order != null && p.Order.User != null ? p.Order.User.Email ?? string.Empty : string.Empty,
                Amount        = p.Amount,
                Provider      = p.Provider,
                TransactionId = p.TransactionId,
                Status        = p.Status,
                CreatedAt     = p.CreatedAt
            })
            .ToList();

        ViewBag.CurrentPage  = page;
        ViewBag.TotalPages   = totalPages;
        ViewBag.TotalCount   = totalCount;
        ViewBag.PageSize     = PageSize;
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
