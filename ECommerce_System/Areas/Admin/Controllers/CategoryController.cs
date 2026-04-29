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
public class CategoryController : Controller
{
    private readonly IUnitOfWork _unitOfWork;
    private const int PageSize = 10;

    public CategoryController(IUnitOfWork unitOfWork)
        => _unitOfWork = unitOfWork;

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Builds SelectList of all categories for the Parent dropdown.</summary>
    private async Task<IEnumerable<SelectListItem>> GetParentDropdownAsync(int excludeId = 0)
    {
        var all = await _unitOfWork.Categories.GetAllAsync(tracked: false);

        return all
            .Where(c => c.Id != excludeId)                  // Prevent self-reference
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text  = c.Name
            });
    }

    /// <summary>Generates a URL-friendly slug from a name.</summary>
    private static string GenerateSlug(string name)
        => name.Trim()
               .ToLowerInvariant()
               .Replace(" ", "-")
               .Replace("'", "")
               .Replace("&", "and");

    // ─── INDEX ─────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Index(int page = 1)
    {
        page = Math.Max(page, 1);
        ViewData["Title"] = "Categories";

        var query = _unitOfWork.Categories.Query().AsNoTracking();
        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        if (page > totalPages)
            page = totalPages;

        var categories = await query
            .AsSplitQuery()
            .Include(c => c.ParentCategory)
            .Include(c => c.SubCategories)
            .Include(c => c.Products)
            .OrderBy(c => c.ParentCategoryId.HasValue)
            .ThenBy(c => c.Name)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var viewModels = categories
            .Select(c => new CategoryVM
            {
                Id                  = c.Id,
                Name                = c.Name,
                Slug                = c.Slug,
                ParentCategoryName  = c.ParentCategory?.Name,
                SubCategoriesCount  = c.SubCategories.Count,
                ProductsCount       = c.Products.Count
            });

        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalCount = totalCount;
        ViewBag.PageSize = PageSize;
        return View(viewModels);
    }

    // ─── DETAILS ───────────────────────────────────────────────────────────────

    public async Task<IActionResult> Details(int id)
    {
        ViewData["Title"] = "Category Details";

        var category = await _unitOfWork.Categories
            .FindAsync(c => c.Id == id, "ParentCategory,SubCategories,Products");

        if (category is null)
            return NotFound();

        var vm = new CategoryVM
        {
            Id                  = category.Id,
            Name                = category.Name,
            Slug                = category.Slug,
            ParentCategoryName  = category.ParentCategory?.Name,
            SubCategoriesCount  = category.SubCategories.Count,
            ProductsCount       = category.Products.Count
        };

        return View(vm);
    }

    // ─── CREATE ────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Create()
    {
        ViewData["Title"] = "New Category";

        var vm = new CategoryFormVM
        {
            ParentCategories = await GetParentDropdownAsync()
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryFormVM vm)
    {
        // Auto-generate slug if empty
        if (string.IsNullOrWhiteSpace(vm.Slug))
            vm.Slug = GenerateSlug(vm.Name);

        if (!ModelState.IsValid)
        {
            vm.ParentCategories = await GetParentDropdownAsync();
            return View(vm);
        }

        // Check slug uniqueness
        var slugExists = await _unitOfWork.Categories
            .FindAsync(c => c.Slug == vm.Slug);

        if (slugExists is not null)
        {
            ModelState.AddModelError(nameof(vm.Slug), "This slug is already used by another category.");
            vm.ParentCategories = await GetParentDropdownAsync();
            return View(vm);
        }

        var category = new Category
        {
            Name             = vm.Name.Trim(),
            Slug             = vm.Slug.Trim(),
            ParentCategoryId = vm.ParentCategoryId
        };

        await _unitOfWork.Categories.AddAsync(category);
        await _unitOfWork.SaveAsync();

        TempData["success"] = $"Category \"{category.Name}\" created successfully.";
        return RedirectToAction(nameof(Index));
    }

    // ─── EDIT ──────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Edit(int id)
    {
        ViewData["Title"] = "Edit Category";

        var category = await _unitOfWork.Categories.GetByIdAsync(id);

        if (category is null)
            return NotFound();

        var vm = new CategoryFormVM
        {
            Id               = category.Id,
            Name             = category.Name,
            Slug             = category.Slug,
            ParentCategoryId = category.ParentCategoryId,
            ParentCategories = await GetParentDropdownAsync(excludeId: id)
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CategoryFormVM vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Slug))
            vm.Slug = GenerateSlug(vm.Name);

        if (!ModelState.IsValid)
        {
            vm.ParentCategories = await GetParentDropdownAsync(excludeId: vm.Id);
            return View(vm);
        }

        // Slug uniqueness — exclude current category
        var slugExists = await _unitOfWork.Categories
            .FindAsync(c => c.Slug == vm.Slug && c.Id != vm.Id);

        if (slugExists is not null)
        {
            ModelState.AddModelError(nameof(vm.Slug), "This slug is already used by another category.");
            vm.ParentCategories = await GetParentDropdownAsync(excludeId: vm.Id);
            return View(vm);
        }

        // Prevent circular reference: category cannot be its own parent
        if (vm.ParentCategoryId == vm.Id)
        {
            ModelState.AddModelError(nameof(vm.ParentCategoryId), "A category cannot be its own parent.");
            vm.ParentCategories = await GetParentDropdownAsync(excludeId: vm.Id);
            return View(vm);
        }

        var category = await _unitOfWork.Categories.GetByIdAsync(vm.Id);

        if (category is null)
            return NotFound();

        category.Name             = vm.Name.Trim();
        category.Slug             = vm.Slug.Trim();
        category.ParentCategoryId = vm.ParentCategoryId;

        _unitOfWork.Categories.Update(category);
        await _unitOfWork.SaveAsync();

        TempData["success"] = $"Category \"{category.Name}\" updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    // ─── DELETE ────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Delete(int id)
    {
        ViewData["Title"] = "Delete Category";

        var category = await _unitOfWork.Categories
            .FindAsync(c => c.Id == id, "ParentCategory,SubCategories,Products");

        if (category is null)
            return NotFound();

        var vm = new CategoryDeleteVM
        {
            Id                  = category.Id,
            Name                = category.Name,
            ParentCategoryName  = category.ParentCategory?.Name,
            SubCategoriesCount  = category.SubCategories.Count,
            ProductsCount       = category.Products.Count
        };

        return View(vm);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var category = await _unitOfWork.Categories
            .FindAsync(c => c.Id == id, "SubCategories,Products");

        if (category is null)
            return NotFound();

        // Guard: cannot delete if it has sub-categories
        if (category.SubCategories.Any())
        {
            TempData["error"] = $"Cannot delete \"{category.Name}\" — it has {category.SubCategories.Count} sub-categories. Delete them first.";
            return RedirectToAction(nameof(Index));
        }

        // Guard: cannot delete if it has products
        if (category.Products.Any())
        {
            TempData["error"] = $"Cannot delete \"{category.Name}\" — it has {category.Products.Count} products. Reassign them first.";
            return RedirectToAction(nameof(Index));
        }

        _unitOfWork.Categories.Remove(category);
        await _unitOfWork.SaveAsync();

        TempData["success"] = $"Category \"{category.Name}\" deleted successfully.";
        return RedirectToAction(nameof(Index));
    }
}
