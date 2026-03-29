using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ECommerce_System.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SD.Role_Admin)]
public class ProductController : Controller
{
    private readonly IUnitOfWork _uow;

    public ProductController(IUnitOfWork uow) => _uow = uow;

    // ─── INDEX ────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Products";

        var products = await _uow.Products
            .GetAllAsync("Category,Variants,Images", tracked: false);

        var vms = products
            .OrderByDescending(p => p.CreatedAt)
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

        return View(vms);
    }

    // ─── DETAILS ──────────────────────────────────────────────────────────────

    public async Task<IActionResult> Details(int id)
    {
        var product = await _uow.Products
            .FindAsync(p => p.Id == id, "Category,Variants,Images");

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
        var product = await _uow.Products.GetByIdAsync(id);
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
    public async Task<IActionResult> Edit(ProductFormVM vm)
    {
        if (!ModelState.IsValid)
        {
            vm.Categories = await GetCategoriesDropdownAsync();
            return View(vm);
        }

        var product = await _uow.Products.GetByIdAsync(vm.Id);
        if (product is null) return NotFound();

        product.Name        = vm.Name.Trim();
        product.Description = vm.Description?.Trim();
        product.BasePrice   = vm.BasePrice;
        product.CategoryId  = vm.CategoryId;
        product.IsActive    = vm.IsActive;
        product.UpdatedAt   = DateTime.UtcNow;

        _uow.Products.Update(product);
        await _uow.SaveAsync();

        TempData["success"] = $"Product \"{product.Name}\" updated.";
        return RedirectToAction(nameof(Details), new { id = product.Id });
    }

    // ─── DELETE ───────────────────────────────────────────────────────────────

    public async Task<IActionResult> Delete(int id)
    {
        var product = await _uow.Products
            .FindAsync(p => p.Id == id, "Category,Variants,Images");
        if (product is null) return NotFound();

        ViewData["Title"] = "Delete Product";
        return View(MapToDetailsVM(product));
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var product = await _uow.Products
            .FindAsync(p => p.Id == id, "Variants");
        if (product is null) return NotFound();

        // Soft-delete (preserves order history)
        product.IsActive  = false;
        product.UpdatedAt = DateTime.UtcNow;
        foreach (var v in product.Variants) v.IsActive = false;

        _uow.Products.Update(product);
        await _uow.SaveAsync();

        TempData["success"] = $"Product \"{product.Name}\" deactivated.";
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
