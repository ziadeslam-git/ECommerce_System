using ECommerce_System.Data;
using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECommerce_System.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SD.Role_Admin)]
public class DashboardController : Controller
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;

    public DashboardController(
        IUnitOfWork unitOfWork,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context)
    {
        _unitOfWork  = unitOfWork;
        _userManager = userManager;
        _context     = context;
    }

    public async Task<IActionResult> Index()
    {
        // ── Sequential awaits required — DbContext is not thread-safe ────────
        var totalProducts  = await _unitOfWork.Products.Query().AsNoTracking().CountAsync(p => p.IsActive);
        var totalOrders    = await _unitOfWork.Orders.Query().AsNoTracking().CountAsync();
        var pendingOrders  = await _unitOfWork.Orders.Query().AsNoTracking().CountAsync(o => o.Status == SD.Status_Pending);
        var revenue        = await _unitOfWork.Payments.Query().AsNoTracking()
                               .Where(p => p.Status == SD.Payment_Paid)
                               .SumAsync(p => (decimal?)p.Amount) ?? 0m;

        // ── Customer count: JOIN on UserRoles table — no full-user-list load ──
        var totalCustomers = await _context.UserRoles
            .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .AsNoTracking()
            .CountAsync(x => x.Name == SD.Role_Customer);

        ViewBag.TotalProducts  = totalProducts;
        ViewBag.TotalOrders    = totalOrders;
        ViewBag.TotalRevenue   = revenue;
        ViewBag.TotalCustomers = totalCustomers;
        ViewBag.PendingOrders  = pendingOrders;

        // ── Recent orders: OrderBy + Take pushed to SQL ─────────────────────────
        ViewBag.RecentOrders = await _unitOfWork.Orders
            .Query()
            .AsNoTracking()
            .Include(o => o.User)
            .Include(o => o.Address)
            .OrderByDescending(o => o.Id)
            .Take(5)
            .ToListAsync();

        // ── Monthly Revenue: single GroupBy query instead of loading all payments ──
        var currentYear = DateTime.UtcNow.Year;
        var monthlyRaw = await _unitOfWork.Payments.Query().AsNoTracking()
            .Where(p => p.Status == SD.Payment_Paid && p.CreatedAt.Year == currentYear)
            .GroupBy(p => p.CreatedAt.Month)
            .Select(g => new { Month = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync();

        var monthlyRevenue = new decimal[12];
        foreach (var m in monthlyRaw)
            monthlyRevenue[m.Month - 1] = m.Total;
        ViewBag.MonthlyRevenue = monthlyRevenue;

        // ── Top Products: full aggregation at DB level — no full table load ──────
        var topProducts = await _unitOfWork.OrderItems
            .Query()
            .AsNoTracking()
            .Where(oi => oi.Order.Status != SD.Status_Cancelled)
            .GroupBy(oi => new
            {
                oi.ProductName,
                CategoryName = oi.ProductVariant.Product.Category != null
                    ? oi.ProductVariant.Product.Category.Name
                    : "General",
                // Grab the main image inside the GroupBy key so SQL can project it
                ImageUrl = oi.ProductVariant.Product.Images
                               .Where(i => i.IsMain)
                               .Select(i => i.ImageUrl)
                               .FirstOrDefault()
                           ?? oi.ProductVariant.Product.Images
                               .OrderBy(i => i.DisplayOrder)
                               .Select(i => i.ImageUrl)
                               .FirstOrDefault()
            })
            .Select(g => new ECommerce_System.ViewModels.Admin.TopProductVM
            {
                ProductName  = g.Key.ProductName,
                CategoryName = g.Key.CategoryName,
                TotalSold    = g.Sum(x => x.Quantity),
                ImageUrl     = g.Key.ImageUrl ?? string.Empty
            })
            .OrderByDescending(x => x.TotalSold)
            .Take(5)
            .ToListAsync();

        ViewBag.TopProducts = topProducts;

        return View();
    }
}
