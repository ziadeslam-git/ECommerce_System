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

    // GET: /Admin/Users
    public async Task<IActionResult> Index(int page = 1)
    {
        var users = await _userManager.Users
            .AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        // Build VMs with roles
        var vms = new List<UserAdminVM>();
        foreach (var user in users)
        {
            var roles  = await _userManager.GetRolesAsync(user);
            var orders = await _unitOfWork.Orders
                .FindAllAsync(o => o.UserId == user.Id, tracked: false);

            vms.Add(new UserAdminVM
            {
                Id            = user.Id,
                FullName      = user.FullName,
                Email         = user.Email ?? string.Empty,
                IsActive      = user.IsActive,
                CreatedAt     = user.CreatedAt,
                Roles         = roles,
                TotalOrders   = orders.Count(),
                LastOrderDate = orders.OrderByDescending(o => o.CreatedAt).FirstOrDefault()?.CreatedAt
            });
        }

        int totalPages = (int)Math.Ceiling(vms.Count / (double)PageSize);
        var paged = vms.Skip((page - 1) * PageSize).Take(PageSize).ToList();

        ViewBag.CurrentPage = page;
        ViewBag.TotalPages  = totalPages;
        ViewData["Title"]   = "Users";
        return View(paged);
    }

    // GET: /Admin/Users/Details/{id}
    public async Task<IActionResult> Details(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var roles  = await _userManager.GetRolesAsync(user);
        var orders = await _unitOfWork.Orders
            .FindAllAsync(o => o.UserId == user.Id, tracked: false);

        var vm = new UserAdminVM
        {
            Id            = user.Id,
            FullName      = user.FullName,
            Email         = user.Email ?? string.Empty,
            IsActive      = user.IsActive,
            CreatedAt     = user.CreatedAt,
            Roles         = roles,
            TotalOrders   = orders.Count(),
            LastOrderDate = orders.OrderByDescending(o => o.CreatedAt).FirstOrDefault()?.CreatedAt
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
