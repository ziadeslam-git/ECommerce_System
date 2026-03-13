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
        // Total active products
        var products = await _unitOfWork.Products
            .FindAllAsync(p => p.IsActive, tracked: false);

        // All orders
        var orders = await _unitOfWork.Orders.GetAllAsync(tracked: false);

        // Recent 5 orders
        var recentOrders = await _unitOfWork.Orders
            .FindAllAsync(o => true, "User,Address", tracked: false);

        // Total revenue (paid orders only)
        var allOrders = orders.ToList();
        var revenue = allOrders
            .Where(o => o.PaymentStatus == SD.Payment_Paid)
            .Sum(o => o.TotalAmount);

        // Customer count
        var customers = await _userManager.GetUsersInRoleAsync(SD.Role_Customer);

        ViewBag.TotalProducts = products.Count();
        ViewBag.TotalOrders   = allOrders.Count;
        ViewBag.TotalRevenue  = revenue;
        ViewBag.TotalCustomers = customers.Count;
        ViewBag.PendingOrders = allOrders.Count(o => o.Status == SD.Status_Pending);
        
        // Pass Recent orders to the view to dynamically populate the dashboard rather than hardcoded UI
        ViewBag.RecentOrders = recentOrders.OrderByDescending(o => o.Id).Take(5).ToList();


        return View();
    }
}
