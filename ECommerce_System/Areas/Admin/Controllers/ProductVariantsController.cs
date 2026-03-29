using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECommerce_System.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SD.Role_Admin)]
public class ProductVariantsController : Controller
{
    private readonly IUnitOfWork _uow;

    public ProductVariantsController(IUnitOfWork uow) => _uow = uow;

    // ─── INDEX — list variants for a product ─────────────────────────────────

    public async Task<IActionResult> Index(int productId)
    {
        var product = await _uow.Products
            .FindAsync(p => p.Id == productId, "Category,Variants,Images");

        if (product is null) return NotFound();

        ViewData["Title"]   = $"Variants — {product.Name}";
        ViewData["ProductId"]   = productId;
        ViewData["ProductName"] = product.Name;

        var detailsVm = MapToDetailsVM(product);
        return View(detailsVm);
    }

    // ─── CREATE ───────────────────────────────────────────────────────────────

    public async Task<IActionResult> Create(int productId)
    {
        var product = await _uow.Products.GetByIdAsync(productId);
        if (product is null) return NotFound();

        ViewData["Title"]       = "Add Variant";
        ViewData["ProductName"] = product.Name;

        return View(new ProductVariantVM
        {
            ProductId   = productId,
            ProductName = product.Name,
            Price       = product.BasePrice,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductVariantVM vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"]       = "Add Variant";
            ViewData["ProductName"] = vm.ProductName;
            return View(vm);
        }

        // Ensure (ProductId + Size + Color) is unique
        var duplicate = await _uow.ProductVariants
            .FindAsync(v => v.ProductId == vm.ProductId
                         && v.Size      == vm.Size
                         && v.Color     == vm.Color);

        if (duplicate is not null)
        {
            ModelState.AddModelError(string.Empty,
                $"A variant with Size={vm.Size} and Color={vm.Color} already exists for this product.");
            ViewData["Title"]       = "Add Variant";
            ViewData["ProductName"] = vm.ProductName;
            return View(vm);
        }

        // Ensure SKU uniqueness
        var skuExists = await _uow.ProductVariants
            .FindAsync(v => v.SKU == vm.SKU.Trim());
        if (skuExists is not null)
        {
            ModelState.AddModelError(nameof(vm.SKU), "This SKU is already used by another variant.");
            ViewData["Title"]       = "Add Variant";
            ViewData["ProductName"] = vm.ProductName;
            return View(vm);
        }

        var variant = new ProductVariant
        {
            ProductId = vm.ProductId,
            Size      = vm.Size.Trim(),
            Color     = vm.Color.Trim(),
            SKU       = vm.SKU.Trim().ToUpperInvariant(),
            Price     = vm.Price,
            Stock     = vm.Stock,
            IsActive  = vm.IsActive,
        };

        await _uow.ProductVariants.AddAsync(variant);
        await _uow.SaveAsync();

        TempData["success"] = $"Variant {variant.Size}/{variant.Color} added.";
        return RedirectToAction(nameof(Index), new { productId = vm.ProductId });
    }

    // ─── EDIT ─────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Edit(int id)
    {
        var variant = await _uow.ProductVariants
            .FindAsync(v => v.Id == id, "Product");

        if (variant is null) return NotFound();

        ViewData["Title"]       = "Edit Variant";
        ViewData["ProductName"] = variant.Product.Name;

        return View(new ProductVariantVM
        {
            Id          = variant.Id,
            ProductId   = variant.ProductId,
            Size        = variant.Size,
            Color       = variant.Color,
            SKU         = variant.SKU,
            Price       = variant.Price,
            Stock       = variant.Stock,
            IsActive    = variant.IsActive,
            RowVersion  = variant.RowVersion,
            ProductName = variant.Product.Name,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ProductVariantVM vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"]       = "Edit Variant";
            ViewData["ProductName"] = vm.ProductName;
            return View(vm);
        }

        var variant = await _uow.ProductVariants.GetByIdAsync(vm.Id);
        if (variant is null) return NotFound();

        // Attach row version for optimistic concurrency
        if (vm.RowVersion is not null)
            _uow.SetRowVersion(variant, vm.RowVersion);

        // (Size + Color + ProductId) unique — exclude self
        var duplicate = await _uow.ProductVariants
            .FindAsync(v => v.ProductId == vm.ProductId
                         && v.Size      == vm.Size
                         && v.Color     == vm.Color
                         && v.Id        != vm.Id);
        if (duplicate is not null)
        {
            ModelState.AddModelError(string.Empty,
                $"A variant with Size={vm.Size} and Color={vm.Color} already exists.");
            ViewData["ProductName"] = vm.ProductName;
            return View(vm);
        }

        // SKU unique — exclude self
        var skuConflict = await _uow.ProductVariants
            .FindAsync(v => v.SKU == vm.SKU.Trim() && v.Id != vm.Id);
        if (skuConflict is not null)
        {
            ModelState.AddModelError(nameof(vm.SKU), "This SKU is already used.");
            ViewData["ProductName"] = vm.ProductName;
            return View(vm);
        }

        try
        {
            variant.Size     = vm.Size.Trim();
            variant.Color    = vm.Color.Trim();
            variant.SKU      = vm.SKU.Trim().ToUpperInvariant();
            variant.Price    = vm.Price;
            variant.Stock    = vm.Stock;
            variant.IsActive = vm.IsActive;

            _uow.ProductVariants.Update(variant);
            await _uow.SaveAsync();

            TempData["success"] = "Variant updated.";
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["error"] = "This variant was modified by someone else. Please reload and try again.";
        }

        return RedirectToAction(nameof(Index), new { productId = vm.ProductId });
    }

    // ─── DELETE ───────────────────────────────────────────────────────────────

    public async Task<IActionResult> Delete(int id)
    {
        var variant = await _uow.ProductVariants
            .FindAsync(v => v.Id == id, "Product");
        if (variant is null) return NotFound();

        ViewData["Title"]       = "Remove Variant";
        ViewData["ProductName"] = variant.Product.Name;

        return View(new ProductVariantVM
        {
            Id          = variant.Id,
            ProductId   = variant.ProductId,
            Size        = variant.Size,
            Color       = variant.Color,
            SKU         = variant.SKU,
            Price       = variant.Price,
            Stock       = variant.Stock,
            IsActive    = variant.IsActive,
            ProductName = variant.Product.Name,
        });
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var variant = await _uow.ProductVariants
            .FindAsync(v => v.Id == id, "CartItems,OrderItems");
        if (variant is null) return NotFound();

        int productId = variant.ProductId;

        if (variant.OrderItems.Any())
        {
            // Soft-delete to preserve order history
            variant.IsActive = false;
            variant.Stock    = 0;
            _uow.ProductVariants.Update(variant);
            TempData["success"] = "Variant deactivated (has order history — not physically deleted).";
        }
        else
        {
            _uow.ProductVariants.Remove(variant);
            TempData["success"] = "Variant deleted.";
        }

        await _uow.SaveAsync();
        return RedirectToAction(nameof(Index), new { productId });
    }

    // ─── TOGGLE ACTIVE ────────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var variant = await _uow.ProductVariants.GetByIdAsync(id);
        if (variant is null) return NotFound();

        variant.IsActive = !variant.IsActive;
        _uow.ProductVariants.Update(variant);
        await _uow.SaveAsync();

        TempData["success"] = $"Variant {(variant.IsActive ? "activated" : "deactivated")}.";
        return RedirectToAction(nameof(Index), new { productId = variant.ProductId });
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

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
        Variants     = p.Variants
            .OrderBy(v => v.Size).ThenBy(v => v.Color)
            .Select(v => new ProductVariantVM
            {
                Id          = v.Id,
                ProductId   = v.ProductId,
                Size        = v.Size,
                Color       = v.Color,
                SKU         = v.SKU,
                Price       = v.Price,
                Stock       = v.Stock,
                IsActive    = v.IsActive,
                RowVersion  = v.RowVersion,
                ProductName = p.Name,
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
