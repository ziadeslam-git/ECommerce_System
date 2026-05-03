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

    public DashboardController(IUnitOfWork unitOfWork, UserManager<ApplicationUser> userManager)
    {
        _unitOfWork  = unitOfWork;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var totalProducts = await _unitOfWork.Products
            .Query()
            .AsNoTracking()
            .CountAsync(p => p.IsActive);

        // All orders & recent orders
        var allOrdersList = (await _unitOfWork.Orders.GetAllAsync(tracked: false)).ToList();
        var recentOrders = await _unitOfWork.Orders
            .FindAllAsync(o => true, "User,Address", tracked: false);
        
        // Accurate Revenue from Payments Table
        var allPaymentsList = (await _unitOfWork.Payments.GetAllAsync(tracked: false)).ToList();

        // Total revenue (paid payments only)
        var revenue = allPaymentsList
            .Where(p => p.Status == SD.Payment_Paid)
            .Sum(p => p.Amount);

        // Customer count
        var customers = await _userManager.GetUsersInRoleAsync(SD.Role_Customer);

        ViewBag.TotalProducts = totalProducts;
        ViewBag.TotalOrders   = allOrdersList.Count;
        ViewBag.TotalRevenue  = revenue;
        ViewBag.TotalCustomers = customers.Count;
        ViewBag.PendingOrders = allOrdersList.Count(o => o.Status == SD.Status_Pending);
        
        // Pass Recent orders 
        ViewBag.RecentOrders = recentOrders.OrderByDescending(o => o.Id).Take(5).ToList();

        // --- DYNAMIC DASHBOARD ADDITIONS ---

        // 1. Monthly Revenue (Paid payments in the current year)
        var currentYear = DateTime.UtcNow.Year;
        var monthlyRevenue = new decimal[12];
        foreach (var p in allPaymentsList.Where(p => p.Status == SD.Payment_Paid && p.CreatedAt.Year == currentYear))
        {
            monthlyRevenue[p.CreatedAt.Month - 1] += p.Amount;
        }
        ViewBag.MonthlyRevenue = monthlyRevenue; // Pass float array to view

        // 2. Top Products (Most sold non-cancelled items)
        var validOrderIds = allOrdersList
            .Where(o => o.Status != SD.Status_Cancelled)
            .Select(o => o.Id)
            .ToHashSet();

        var allOrderItems = await _unitOfWork.OrderItems
            .GetAllAsync(includeProperties: "ProductVariant,ProductVariant.Product,ProductVariant.Product.Category,ProductVariant.Product.Images", tracked: false);

        var topProducts = allOrderItems
            .Where(oi => validOrderIds.Contains(oi.OrderId))
            .GroupBy(oi => new { oi.ProductName, CategoryName = oi.ProductVariant?.Product?.Category?.Name ?? "General" })
            .Select(g => new ECommerce_System.ViewModels.Admin.TopProductVM
            {
                ProductName = g.Key.ProductName,
                CategoryName = g.Key.CategoryName,
                TotalSold = g.Sum(x => x.Quantity),
                ImageUrl = g.FirstOrDefault()?.ProductVariant?.Product?.Images?.FirstOrDefault(i => i.IsMain)?.ImageUrl ?? ""
            })
            .OrderByDescending(x => x.TotalSold)
            .Take(5)
            .ToList();

        ViewBag.TopProducts = topProducts;

        return View();
    }
}
