using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce_System.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SD.Role_Admin)]
public class DiscountsController : Controller
{
    private readonly IUnitOfWork _unitOfWork;
    private const int PageSize = 10;

    public DiscountsController(IUnitOfWork unitOfWork)
        => _unitOfWork = unitOfWork;

    // GET: /Admin/Discounts
    public async Task<IActionResult> Index(int page = 1, string? searchQuery = null, string? typeFilter = null)
    {
        // TODO: Replace with GetPagedAsync when implemented in IRepository<T>
        // to push Skip/Take/filter to database level and avoid loading all discounts.
        var allDiscounts = await _unitOfWork.Discounts.GetAllAsync(tracked: false);

        // Apply filters + ordering + Skip/Take in a single LINQ chain on IQueryable —
        // VM projection happens AFTER Skip/Take so only PageSize objects are created.
        var query = allDiscounts.AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchQuery))
            query = query.Where(d => d.CouponCode.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(typeFilter))
            query = query.Where(d => d.Type == typeFilter);

        int totalCount = query.Count();
        int totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);

        var paged = query
            .OrderByDescending(d => d.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(d => new DiscountVM
            {
                Id                 = d.Id,
                CouponCode         = d.CouponCode,
                Type               = d.Type,
                DiscountValue      = d.Value,
                MinOrderValue      = d.MinimumOrderAmount,
                UsageLimit         = d.UsageLimit,
                UsageCount         = d.UsageCount,
                StartDate          = null,
                EndDate            = d.ExpiresAt,
                IsActive           = d.IsActive
            })
            .ToList();

        ViewBag.CurrentPage = page;
        ViewBag.TotalPages  = totalPages;
        ViewBag.SearchQuery = searchQuery;
        ViewBag.TypeFilter  = typeFilter;
        ViewData["Title"]   = "Discounts";

        return View(paged);
    }

    // GET: /Admin/Discounts/Create
    public IActionResult Create()
    {
        ViewData["Title"] = "Create Discount";
        var defaultVm = new DiscountVM {
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(7)
        };
        return View(defaultVm);
    }

    // POST: /Admin/Discounts/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DiscountVM vm)
    {
        // Server-side type validation
        if (vm.Type != SD.Discount_Percentage && vm.Type != SD.Discount_FixedAmount)
            ModelState.AddModelError(nameof(vm.Type), "Type must be 'Percentage' or 'FixedAmount'.");

        if (vm.Type == SD.Discount_Percentage && (vm.DiscountValue < 1 || vm.DiscountValue > 100))
            ModelState.AddModelError(nameof(vm.DiscountValue), "Percentage value must be between 1 and 100.");

        // Uppercase coupon code safely
        vm.CouponCode = vm.CouponCode?.ToUpperInvariant() ?? string.Empty;

        if (ModelState.IsValid)
        {
            // Check uniqueness
            var existing = await _unitOfWork.Discounts
                .FindAsync(d => d.CouponCode == vm.CouponCode);

            if (existing != null)
            {
                ModelState.AddModelError(nameof(vm.CouponCode), "This coupon code already exists.");
            }
            else
            {
                var entity = new Discount
                {
                    CouponCode         = vm.CouponCode,
                    Type               = vm.Type,
                    Value              = vm.DiscountValue,
                    MinimumOrderAmount = vm.MinOrderValue,
                    UsageLimit         = vm.UsageLimit,
                    UsageCount         = 0, // always starts at 0
                    ExpiresAt          = vm.EndDate,
                    IsActive           = vm.IsActive
                };

                await _unitOfWork.Discounts.AddAsync(entity);
                await _unitOfWork.SaveAsync();

                TempData["success"] = $"Coupon '{entity.CouponCode}' created successfully.";
                return RedirectToAction(nameof(Index));
            }
        }

        ViewData["Title"] = "Create Discount";
        return View(vm);
    }

    // GET: /Admin/Discounts/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var entity = await _unitOfWork.Discounts.GetByIdAsync(id);
        if (entity == null) return NotFound();

        var vm = new DiscountVM
        {
            Id                 = entity.Id,
            CouponCode         = entity.CouponCode,
            Type               = entity.Type,
            DiscountValue      = entity.Value,
            MinOrderValue      = entity.MinimumOrderAmount,
            UsageLimit         = entity.UsageLimit,
            UsageCount         = entity.UsageCount,
            StartDate          = null,
            EndDate            = entity.ExpiresAt ?? DateTime.MaxValue,
            IsActive           = entity.IsActive
        };

        ViewData["Title"] = "Edit Discount";
        return View(vm);
    }

    // GET: /Admin/Discounts/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var entity = await _unitOfWork.Discounts.GetByIdAsync(id);
        if (entity == null) return NotFound();

        var vm = new DiscountVM
        {
            Id                 = entity.Id,
            CouponCode         = entity.CouponCode,
            Type               = entity.Type,
            DiscountValue      = entity.Value,
            MinOrderValue      = entity.MinimumOrderAmount,
            UsageLimit         = entity.UsageLimit,
            UsageCount         = entity.UsageCount,
            StartDate          = null,
            EndDate            = entity.ExpiresAt,
            IsActive           = entity.IsActive
        };

        ViewData["Title"] = "Discount Details";
        return View(vm);
    }

    // POST: /Admin/Discounts/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, DiscountVM vm)
    {
        if (id != vm.Id) return BadRequest();

        // Server-side type validation
        if (vm.Type != SD.Discount_Percentage && vm.Type != SD.Discount_FixedAmount)
            ModelState.AddModelError(nameof(vm.Type), "Type must be 'Percentage' or 'FixedAmount'.");

        if (vm.Type == SD.Discount_Percentage && (vm.DiscountValue < 1 || vm.DiscountValue > 100))
            ModelState.AddModelError(nameof(vm.DiscountValue), "Percentage value must be between 1 and 100.");

        vm.CouponCode = vm.CouponCode?.ToUpperInvariant() ?? string.Empty;

        if (ModelState.IsValid)
        {
            var entity = await _unitOfWork.Discounts.GetByIdAsync(id);
            if (entity == null) return NotFound();

            // Check uniqueness (excluding self)
            var duplicate = await _unitOfWork.Discounts
                .FindAsync(d => d.CouponCode == vm.CouponCode && d.Id != id);

            if (duplicate != null)
            {
                ModelState.AddModelError(nameof(vm.CouponCode), "This coupon code already exists.");
                ViewData["Title"] = "Edit Discount";
                return View(vm);
            }

            // NEVER update UsageCount from form
            entity.CouponCode         = vm.CouponCode;
            entity.Type               = vm.Type;
            entity.Value              = vm.DiscountValue;
            entity.MinimumOrderAmount = vm.MinOrderValue;
            entity.UsageLimit         = vm.UsageLimit;
            entity.ExpiresAt          = vm.EndDate;
            entity.IsActive           = vm.IsActive;

            _unitOfWork.Discounts.Update(entity);
            await _unitOfWork.SaveAsync();

            TempData["success"] = $"Coupon '{entity.CouponCode}' updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = "Edit Discount";
        return View(vm);
    }

    // GET: /Admin/Discounts/Delete/5
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _unitOfWork.Discounts.GetByIdAsync(id);
        if (entity == null) return NotFound();

        var vm = new DiscountVM
        {
            Id                 = entity.Id,
            CouponCode         = entity.CouponCode,
            Type               = entity.Type,
            DiscountValue      = entity.Value,
            MinOrderValue      = entity.MinimumOrderAmount,
            UsageLimit         = entity.UsageLimit,
            UsageCount         = entity.UsageCount,
            StartDate          = null,
            EndDate            = entity.ExpiresAt,
            IsActive           = entity.IsActive
        };

        ViewData["Title"] = "Delete Discount";
        return View(vm);
    }

    // POST: /Admin/Discounts/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var entity = await _unitOfWork.Discounts.GetByIdAsync(id);
        if (entity == null) return NotFound();

        // Soft delete — preserve coupon history
        entity.IsActive = false;
        _unitOfWork.Discounts.Update(entity);
        await _unitOfWork.SaveAsync();

        TempData["success"] = $"Coupon '{entity.CouponCode}' has been deactivated.";
        return RedirectToAction(nameof(Index));
    }

    // POST: /Admin/Discounts/ToggleActive/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var entity = await _unitOfWork.Discounts.GetByIdAsync(id);
        if (entity == null) return NotFound();

        entity.IsActive = !entity.IsActive;
        _unitOfWork.Discounts.Update(entity);
        await _unitOfWork.SaveAsync();

        TempData["success"] = $"Coupon '{entity.CouponCode}' is now {(entity.IsActive ? "active" : "inactive")}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ExportWord()
    {
        var discounts = await _unitOfWork.Discounts.GetAllAsync(tracked: false);
        
        var sb = new System.Text.StringBuilder();
        sb.Append("<html xmlns:o='urn:schemas-microsoft-com:office:office' xmlns:w='urn:schemas-microsoft-com:office:word' xmlns='http://www.w3.org/TR/REC-html40'>");
        sb.Append("<head><meta charset='utf-8'><title>Discounts Export</title>");
        sb.Append("<style>");
        sb.Append("body { font-family: 'Inter', sans-serif; color: #0b1c30; }");
        sb.Append("h1 { color: #4648d4; text-align: center; font-family: 'Inter', sans-serif; margin-bottom: 20px; }");
        sb.Append("table { width: 100%; border-collapse: collapse; margin-top: 20px; border: 1px solid #e0e3e5; }");
        sb.Append("th { background-color: #f2f4f6; color: #464554; padding: 12px; text-align: left; border: 1px solid #e0e3e5; font-size: 14px; text-transform: uppercase; }");
        sb.Append("td { padding: 12px; border: 1px solid #e0e3e5; color: #0b1c30; font-size: 14px; }");
        sb.Append(".active { color: #059669; font-weight: bold; }");
        sb.Append(".inactive { color: #ba1a1a; font-weight: bold; }");
        sb.Append(".expired { text-decoration: line-through; color: #767586; }");
        sb.Append("</style>");
        sb.Append("</head><body>");
        sb.Append("<h1>Discounts & Coupons Report</h1>");
        sb.Append("<table>");
        sb.Append("<tr><th>Coupon Code</th><th>Type</th><th>Value</th><th>Min Order</th><th>Usage</th><th>Expires</th><th>Status</th></tr>");
        
        foreach (var d in discounts)
        {
            string val = d.Type == "Percentage" ? $"{d.Value:F0}%" : $"${d.Value:F2}";
            string minOrder = d.MinimumOrderAmount.HasValue ? $"${d.MinimumOrderAmount.Value:F2}" : "$0.00";
            string usage = $"{d.UsageCount} / {(d.UsageLimit.HasValue ? d.UsageLimit.Value.ToString() : "∞")}";
            bool isExpired = d.ExpiresAt.HasValue && d.ExpiresAt.Value < DateTime.UtcNow;
            string status = isExpired ? "<span class='inactive'>Expired</span>" : (d.IsActive ? "<span class='active'>Active</span>" : "<span class='inactive'>Inactive</span>");
            string codeCss = isExpired ? "expired" : "";

            sb.Append($"<tr><td class='{codeCss}'><strong>{d.CouponCode}</strong></td><td>{d.Type}</td><td>{val}</td><td>{minOrder}</td><td>{usage}</td><td>{(d.ExpiresAt.HasValue ? d.ExpiresAt.Value.ToString("MMM dd, yyyy") : "Never")}</td><td>{status}</td></tr>");
        }
        
        sb.Append("</table></body></html>");
        
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "application/msword", "Discounts_Export.doc");
    }
}
