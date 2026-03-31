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
    public async Task<IActionResult> Index(int page = 1, string? status = null)
    {
        var payments = await _unitOfWork.Payments
            .GetAllAsync("Order,Order.User", tracked: false);

        if (!string.IsNullOrWhiteSpace(status))
            payments = payments.Where(p => p.Status == status);

        var ordered = payments.OrderByDescending(p => p.CreatedAt).ToList();

        int totalCount = ordered.Count;
        int totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);

        var paged = ordered
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
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

        ViewBag.CurrentPage   = page;
        ViewBag.TotalPages    = totalPages;
        ViewBag.StatusFilter  = status;
        ViewData["Title"]     = "Payments";
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

    // NO Create, Edit, Delete — Payments are managed by Stripe webhooks only
}
