using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ECommerce_System.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SD.Role_Admin)]
public class ProductController : Controller
{
    private readonly IUnitOfWork _uow;
    private const int PageSize = 10;

    public ProductController(IUnitOfWork uow) => _uow = uow;

    // ─── INDEX ────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Index(int page = 1)
    {
        page = Math.Max(page, 1);
        ViewData["Title"] = "Products";

        var query = _uow.Products
            .Query()
            .IgnoreQueryFilters()
            .AsNoTracking();

        var totalCount = await query.CountAsync();
        var activeCount = await query.CountAsync(p => p.IsActive);
        var totalStock = await _uow.ProductVariants.Query()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SumAsync(v => (int?)v.Stock) ?? 0;
        var withImages = await _uow.ProductImages.Query()
            .AsNoTracking()
            .Select(i => i.ProductId)
            .Distinct()
            .CountAsync();

        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        if (page > totalPages)
            page = totalPages;

        var products = await query
            .AsSplitQuery()
            .Include(p => p.Category)
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var vms = products
            .Select(p => new ProductIndexVM
            {
                Id           = p.Id,
                Name         = p.Name,
                BasePrice    = p.BasePrice,
                CategoryName = p.Category?.Name ?? "—",
                IsActive     = p.IsActive,
                AverageRating = p.AverageRating,
                VariantCount = p.Variants.Count,
                ImageCount   = p.Images.Count,
                TotalStock   = p.Variants.Sum(v => v.Stock),
                MainImageUrl = p.Images.FirstOrDefault(i => i.IsMain)?.ImageUrl
                            ?? p.Images.OrderBy(i => i.DisplayOrder).FirstOrDefault()?.ImageUrl,
                CreatedAt    = p.CreatedAt,
            });

        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalCount = totalCount;
        ViewBag.PageSize = PageSize;
        ViewBag.ActiveCount = activeCount;
        ViewBag.TotalStock = totalStock;
        ViewBag.WithImages = withImages;
        return View(vms);
    }

    // ─── DETAILS ──────────────────────────────────────────────────────────────

    public async Task<IActionResult> Details(int id)
    {
        var product = await _uow.Products
            .Query()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Images)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product is null) return NotFound();

        ViewData["Title"] = product.Name;

        var vm = MapToDetailsVM(product);
        return View(vm);
    }

    // ─── CREATE ───────────────────────────────────────────────────────────────

    public async Task<IActionResult> Create()
    {
        ViewData["Title"] = "New Product";
        var vm = new ProductFormVM
        {
            Categories = await GetCategoriesDropdownAsync()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductFormVM vm)
    {
        if (!ModelState.IsValid)
        {
            vm.Categories = await GetCategoriesDropdownAsync();
            return View(vm);
        }

        var product = new Product
        {
            Name        = vm.Name.Trim(),
            Description = vm.Description?.Trim(),
            BasePrice   = vm.BasePrice,
            CategoryId  = vm.CategoryId,
            IsActive    = vm.IsActive,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };

        await _uow.Products.AddAsync(product);
        await _uow.SaveAsync();

        TempData["success"] = $"Product \"{product.Name}\" created successfully.";
        return RedirectToAction(nameof(Details), new { id = product.Id });
    }

    // ─── EDIT ─────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Edit(int id)
    {
        var product = await _uow.Products.GetByIdAsync(id, ignoreQueryFilters: true);
        if (product is null) return NotFound();

        ViewData["Title"] = $"Edit — {product.Name}";

        var vm = new ProductFormVM
        {
            Id          = product.Id,
            Name        = product.Name,
            Description = product.Description,
            BasePrice   = product.BasePrice,
            CategoryId  = product.CategoryId,
            IsActive    = product.IsActive,
            Categories  = await GetCategoriesDropdownAsync(),
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductFormVM vm)
    {
        if (id != vm.Id) return BadRequest();

        if (!ModelState.IsValid)
        {
            vm.Categories = await GetCategoriesDropdownAsync();
            return View(vm);
        }

        var product = await _uow.Products.GetByIdAsync(vm.Id, ignoreQueryFilters: true);
        if (product is null) return NotFound();

        product.Name        = vm.Name.Trim();
        product.Description = vm.Description?.Trim();
        product.BasePrice   = vm.BasePrice;
        product.CategoryId  = vm.CategoryId;
        
        bool wasActive = product.IsActive;
        product.IsActive    = vm.IsActive;
        product.UpdatedAt   = DateTime.UtcNow;

        if (wasActive && !product.IsActive)
        {
            var variants = await _uow.ProductVariants.FindAllAsync(v => v.ProductId == product.Id);
            foreach (var variant in variants)
            {
                variant.IsActive = false;
            }
        }
        else if (!wasActive && product.IsActive)
        {
            var variants = await _uow.ProductVariants.FindAllAsync(v => v.ProductId == product.Id);
            foreach (var variant in variants)
            {
                if (variant.Stock > 0)
                {
                    variant.IsActive = true;
                }
            }
        }

        try
        {
            // _uow.Products.Update(product) is skipped here because GetByIdAsync is tracked
            await _uow.SaveAsync();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            if (await _uow.Products.GetByIdAsync(vm.Id) is null)
                return NotFound();

            ModelState.AddModelError(string.Empty, "The product was modified by another user. Please reload and try again.");
            vm.Categories = await GetCategoriesDropdownAsync();
            return View(vm);
        }

        TempData["success"] = $"Product \"{product.Name}\" updated.";
        return RedirectToAction(nameof(Details), new { id = product.Id });
    }

    // ─── DELETE ───────────────────────────────────────────────────────────────

    public async Task<IActionResult> Delete(int id)
    {
        var product = await _uow.Products
            .Query()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Images)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return NotFound();

        ViewData["Title"] = "Delete Product";
        return View(MapToDetailsVM(product));
    }

    // Soft-delete: Deactivate only (keeps data in DB)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateConfirmed(int id)
    {
        var product = await _uow.Products
            .FindAsync(p => p.Id == id, "Variants", ignoreQueryFilters: true);
        if (product is null) return NotFound();

        product.IsActive  = false;
        product.UpdatedAt = DateTime.UtcNow;
        foreach (var v in product.Variants) v.IsActive = false;

        await _uow.SaveAsync();

        TempData["success"] = $"Product \"{product.Name}\" deactivated. It no longer shows to customers.";
        return RedirectToAction(nameof(Index));
    }

    // FIX #5: Hard-delete path removed — only soft-delete (deactivation) is allowed
    //         to maintain data integrity and order history consistency.


    // ─── TOGGLE ACTIVE ────────────────────────────────────────────────────────
    
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var product = await _uow.Products.GetByIdAsync(id, ignoreQueryFilters: true);
        if (product is null) return NotFound();

        product.IsActive = !product.IsActive;
        product.UpdatedAt = DateTime.UtcNow;

        var variants = await _uow.ProductVariants.FindAllAsync(v => v.ProductId == product.Id, ignoreQueryFilters: true);
        
        if (!product.IsActive)
        {
            foreach (var variant in variants) variant.IsActive = false;
            TempData["success"] = $"Product \"{product.Name}\" deactivated. All variants deactivated.";
        }
        else
        {
            int activatedCount = 0;
            foreach (var variant in variants)
            {
                if (variant.Stock > 0)
                {
                    variant.IsActive = true;
                    activatedCount++;
                }
            }
            TempData["success"] = $"Product \"{product.Name}\" activated. {activatedCount} variant(s) activated.";
        }

        await _uow.SaveAsync();

        return RedirectToAction(nameof(Index));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<IEnumerable<SelectListItem>> GetCategoriesDropdownAsync()
    {
        var cats = await _uow.Categories.GetAllAsync(tracked: false);
        return cats.OrderBy(c => c.Name)
                   .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name });
    }

    private static ProductDetailsVM MapToDetailsVM(Product p) => new()
    {
        Id           = p.Id,
        Name         = p.Name,
        Description  = p.Description,
        BasePrice    = p.BasePrice,
        CategoryName = p.Category?.Name ?? "—",
        IsActive     = p.IsActive,
        AverageRating = p.AverageRating,
        CreatedAt    = p.CreatedAt,
        UpdatedAt    = p.UpdatedAt,
        Variants     = p.Variants.Select(v => new ProductVariantVM
        {
            Id            = v.Id,
            ProductId     = v.ProductId,
            Size          = v.Size,
            Color         = v.Color,
            SKU           = v.SKU,
            Price         = v.Price,
            Stock         = v.Stock,
            IsActive      = v.IsActive,
            RowVersion    = v.RowVersion,
            ProductName   = p.Name,
        }).ToList(),
        Images = p.Images.OrderBy(i => i.DisplayOrder).Select(i => new ProductImageVM
        {
            Id           = i.Id,
            ProductId    = i.ProductId,
            ImageUrl     = i.ImageUrl,
            PublicId     = i.PublicId,
            IsMain       = i.IsMain,
            DisplayOrder = i.DisplayOrder,
            ProductName  = p.Name,
        }).ToList(),
    };
}
