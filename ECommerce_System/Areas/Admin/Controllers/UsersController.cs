using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECommerce_System.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SD.Role_Admin)]
public class UsersController : Controller
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<ApplicationUser> _userManager;
    private const int PageSize = 15;

    public UsersController(IUnitOfWork unitOfWork, UserManager<ApplicationUser> userManager)
    {
        _unitOfWork  = unitOfWork;
        _userManager = userManager;
    }

    // GET: /Admin/Users — DB-level pagination: filters + Skip/Take pushed to SQL before materialization
    public async Task<IActionResult> Index(int page = 1, string? searchQuery = null, string? statusFilter = null)
    {
        // 1. Build IQueryable with all filters applied at DB level
        var query = _userManager.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchQuery))
            query = query.Where(u => u.FullName.Contains(searchQuery) || (u.Email != null && u.Email.Contains(searchQuery)));

        if (statusFilter == "Active")
            query = query.Where(u => u.IsActive);
        else if (statusFilter == "Inactive")
            query = query.Where(u => !u.IsActive);

        // 2. Count at DB level (no data transferred)
        int totalCount = await query.CountAsync();
        int totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);

        // 3. Fetch only the current page's users from DB
        var pagedUsers = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        // 4. Batch-load orders + reviews only for the current page's user IDs
        var pageUserIds = pagedUsers.Select(u => u.Id).ToHashSet();

        var pageOrders = await _unitOfWork.Orders
            .FindAllAsync(o => pageUserIds.Contains(o.UserId), tracked: false);
        var ordersByUser = pageOrders
            .GroupBy(o => o.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var pageReviews = await _unitOfWork.Reviews
            .FindAllAsync(r => pageUserIds.Contains(r.UserId), tracked: false);
        var reviewsByUser = pageReviews
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Count());

        // 5. Build VMs only for this page's users (max PageSize = 15 iterations)
        var vms = new List<UserAdminVM>();
        foreach (var user in pagedUsers)
        {
            var roles      = await _userManager.GetRolesAsync(user);
            var userOrders = ordersByUser.TryGetValue(user.Id, out var ord) ? ord : new();

            vms.Add(new UserAdminVM
            {
                Id            = user.Id,
                FullName      = user.FullName,
                Email         = user.Email ?? string.Empty,
                IsActive      = user.IsActive,
                CreatedAt     = user.CreatedAt,
                Roles         = roles,
                TotalOrders   = userOrders.Count,
                TotalSpent    = userOrders.Sum(o => o.TotalAmount),
                TotalReviews  = reviewsByUser.TryGetValue(user.Id, out var rc) ? rc : 0,
                LastOrderDate = userOrders.OrderByDescending(o => o.CreatedAt).FirstOrDefault()?.CreatedAt
            });
        }

        ViewBag.CurrentPage   = page;
        ViewBag.TotalPages    = totalPages;
        ViewBag.SearchQuery   = searchQuery;
        ViewBag.StatusFilter  = statusFilter;
        ViewData["Title"]     = "Users";
        return View(vms);
    }

    // GET: /Admin/Users/Details/{id} — BUG #2: populate stats + recent orders
    public async Task<IActionResult> Details(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var roles  = await _userManager.GetRolesAsync(user);
        var orders = await _unitOfWork.Orders
            .FindAllAsync(o => o.UserId == user.Id, tracked: false);
        var orderList = orders.ToList();

        var reviews = await _unitOfWork.Reviews
            .FindAllAsync(r => r.UserId == user.Id, tracked: false);

        var vm = new UserAdminVM
        {
            Id            = user.Id,
            FullName      = user.FullName,
            Email         = user.Email ?? string.Empty,
            IsActive      = user.IsActive,
            CreatedAt     = user.CreatedAt,
            Roles         = roles,
            TotalOrders   = orderList.Count,
            TotalSpent    = orderList.Sum(o => o.TotalAmount),
            TotalReviews  = reviews.Count(),
            LastOrderDate = orderList.OrderByDescending(o => o.CreatedAt).FirstOrDefault()?.CreatedAt,
            RecentOrders  = orderList
                .OrderByDescending(o => o.CreatedAt)
                .Take(5)
                .Select(o => new OrderSummaryForUserVM
                {
                    Id            = o.Id,
                    TotalAmount   = o.TotalAmount,
                    Status        = o.Status,
                    PaymentStatus = o.PaymentStatus,
                    CreatedAt     = o.CreatedAt
                }).ToList()
        };

        ViewData["Title"] = "User Details";
        return View(vm);
    }

    // POST: /Admin/Users/ToggleActive/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        // NEVER deactivate yourself (logged-in Admin)
        var currentUserId = _userManager.GetUserId(User);
        if (user.Id == currentUserId)
        {
            TempData["error"] = "You cannot deactivate your own account.";
            return RedirectToAction(nameof(Index));
        }

        user.IsActive = !user.IsActive;
        await _userManager.UpdateAsync(user);

        // Invalidate existing cookie session immediately when deactivating
        if (!user.IsActive)
            await _userManager.UpdateSecurityStampAsync(user);

        TempData["success"] = $"User '{user.FullName}' is now {(user.IsActive ? "active" : "inactive")}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ExportWord()
    {
        var users = await _userManager.Users.AsNoTracking().OrderByDescending(u => u.CreatedAt).ToListAsync();
        
        var sb = new System.Text.StringBuilder();
        sb.Append("<html xmlns:o='urn:schemas-microsoft-com:office:office' xmlns:w='urn:schemas-microsoft-com:office:word' xmlns='http://www.w3.org/TR/REC-html40'>");
        sb.Append("<head><meta charset='utf-8'><title>Customers Export</title>");
        sb.Append("<style>");
        sb.Append("body { font-family: 'Inter', sans-serif; color: #191c1e; }");
        sb.Append("h1 { color: #4648d4; text-align: center; font-family: 'Inter', sans-serif; margin-bottom: 20px; }");
        sb.Append("table { width: 100%; border-collapse: collapse; margin-top: 20px; border: 1px solid #e0e3e5; }");
        sb.Append("th { background-color: #f2f4f6; color: #464554; padding: 12px; text-align: left; border: 1px solid #e0e3e5; font-size: 14px; text-transform: uppercase; }");
        sb.Append("td { padding: 12px; border: 1px solid #e0e3e5; color: #191c1e; font-size: 14px; }");
        sb.Append(".active { color: #059669; font-weight: bold; background-color: #d1fae5; padding: 4px 8px; border-radius: 4px; }");
        sb.Append(".inactive { color: #ba1a1a; font-weight: bold; background-color: #fee2e2; padding: 4px 8px; border-radius: 4px; }");
        sb.Append("</style>");
        sb.Append("</head><body>");
        sb.Append("<h1>Customers Report</h1>");
        sb.Append("<table>");
        sb.Append("<tr><th>Customer Name</th><th>Email Address</th><th>Status</th><th>Joined Date</th></tr>");
        
        foreach (var u in users)
        {
            var status = u.IsActive ? "<span class='active'>Active</span>" : "<span class='inactive'>Inactive</span>";
            sb.Append($"<tr><td>{u.FullName}</td><td>{u.Email}</td><td>{status}</td><td>{u.CreatedAt:MMM dd, yyyy}</td></tr>");
        }
        
        sb.Append("</table></body></html>");
        
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "application/msword", "Customers_Export.doc");
    }
}
