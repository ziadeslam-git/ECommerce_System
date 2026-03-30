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
    private readonly ICloudinaryService _cloudinaryService;

    public ProductVariantsController(IUnitOfWork uow, ICloudinaryService cloudinaryService)
    {
        _uow = uow;
        _cloudinaryService = cloudinaryService;
    }

    // ─── INDEX — list variants for a product ─────────────────────────────────

    public async Task<IActionResult> Index(int productId)
    {
        var product = await _uow.Products
            .FindAsync(p => p.Id == productId, "Category,Variants,Images", ignoreQueryFilters: true);

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
        var product = await _uow.Products.GetByIdAsync(productId, ignoreQueryFilters: true);
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
                         && v.Color     == vm.Color,
                         ignoreQueryFilters: true);

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
            .FindAsync(v => v.SKU == vm.SKU.Trim(), ignoreQueryFilters: true);
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
            IsActive  = vm.Stock > 0 ? vm.IsActive : false,
        };

        if (vm.ImageFiles is not null && vm.ImageFiles.Any())
        {
            var images = new List<ProductVariantImage>();
            bool isFirst = true;

            foreach (var file in vm.ImageFiles)
            {
                var res = await _cloudinaryService.UploadAsync(file, "variants");
                images.Add(new ProductVariantImage
                {
                    ImageUrl = res.Url,
                    PublicId = res.PublicId,
                    IsMain = isFirst
                });

                if (isFirst && vm.SetAsMainProductImage)
                {
                    await SetVariantImageAsMainProductImage(variant.ProductId, res.Url, res.PublicId);
                }
                isFirst = false;
            }
            variant.Images = images;
        }

        await _uow.ProductVariants.AddAsync(variant);
        await _uow.SaveAsync();

        await UpdateProductStatusAsync(variant.ProductId);

        TempData["success"] = $"Variant {variant.Size}/{variant.Color} added.";
        return RedirectToAction(nameof(Index), new { productId = vm.ProductId });
    }

    // ─── EDIT ─────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Edit(int id)
    {
        var variant = await _uow.ProductVariants
            .FindAsync(v => v.Id == id, "Product,Images", ignoreQueryFilters: true);

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
            Images      = variant.Images?.Select(i => new ProductVariantImageVM
            {
                Id = i.Id,
                ImageUrl = i.ImageUrl,
                PublicId = i.PublicId,
                IsMain = i.IsMain
            }).ToList() ?? new List<ProductVariantImageVM>()
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

        var variant = await _uow.ProductVariants.GetByIdAsync(vm.Id, ignoreQueryFilters: true);
        if (variant is null) return NotFound();

        // Attach row version for optimistic concurrency
        if (vm.RowVersion is not null)
            _uow.SetRowVersion(variant, vm.RowVersion);

        // (Size + Color + ProductId) unique — exclude self
        var duplicate = await _uow.ProductVariants
            .FindAsync(v => v.ProductId == vm.ProductId
                         && v.Size      == vm.Size
                         && v.Color     == vm.Color
                         && v.Id        != vm.Id,
                         ignoreQueryFilters: true);
        if (duplicate is not null)
        {
            ModelState.AddModelError(string.Empty,
                $"A variant with Size={vm.Size} and Color={vm.Color} already exists.");
            ViewData["ProductName"] = vm.ProductName;
            return View(vm);
        }

        // SKU unique — exclude self
        var skuConflict = await _uow.ProductVariants
            .FindAsync(v => v.SKU == vm.SKU.Trim() && v.Id != vm.Id, ignoreQueryFilters: true);
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
            variant.IsActive = vm.Stock > 0 ? vm.IsActive : false;

            if (vm.ImageFiles is not null && vm.ImageFiles.Any())
            {
                if (variant.Images != null && variant.Images.Any())
                {
                    foreach(var img in variant.Images)
                    {
                        if(!string.IsNullOrEmpty(img.PublicId))
                            await _cloudinaryService.DeleteAsync(img.PublicId);
                    }
                    variant.Images.Clear();
                }
                else
                {
                    variant.Images = new List<ProductVariantImage>();
                }
                
                bool isFirst = true;
                foreach (var file in vm.ImageFiles)
                {
                    var res = await _cloudinaryService.UploadAsync(file, "variants");
                    variant.Images.Add(new ProductVariantImage
                    {
                        ImageUrl = res.Url,
                        PublicId = res.PublicId,
                        IsMain = isFirst
                    });

                    if (isFirst && vm.SetAsMainProductImage)
                    {
                        await SetVariantImageAsMainProductImage(variant.ProductId, res.Url, res.PublicId);
                    }
                    isFirst = false;
                }
            }
            else if (vm.SetAsMainProductImage && variant.Images != null && variant.Images.Any())
            {
                var mainImg = variant.Images.FirstOrDefault(i => i.IsMain) ?? variant.Images.First();
                await SetVariantImageAsMainProductImage(variant.ProductId, mainImg.ImageUrl, mainImg.PublicId);
            }

            _uow.ProductVariants.Update(variant);
            await _uow.SaveAsync();
            
            await UpdateProductStatusAsync(variant.ProductId);

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
            .FindAsync(v => v.Id == id, "Product,Images", ignoreQueryFilters: true);
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
            Images      = variant.Images?.Select(i => new ProductVariantImageVM
            {
                Id = i.Id,
                ImageUrl = i.ImageUrl,
                PublicId = i.PublicId,
                IsMain = i.IsMain
            }).ToList() ?? new List<ProductVariantImageVM>()
        });
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var variant = await _uow.ProductVariants
            .FindAsync(v => v.Id == id, "CartItems,OrderItems", ignoreQueryFilters: true);
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
        var variant = await _uow.ProductVariants.GetByIdAsync(id, ignoreQueryFilters: true);
        if (variant is null) return NotFound();

        variant.IsActive = !variant.IsActive;
        _uow.ProductVariants.Update(variant);
        await _uow.SaveAsync();

        TempData["success"] = $"Variant {(variant.IsActive ? "activated" : "deactivated")}.";
        return RedirectToAction(nameof(Index), new { productId = variant.ProductId });
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task SetVariantImageAsMainProductImage(int productId, string imageUrl, string publicId)
    {
        var existingImages = await _uow.ProductImages.FindAllAsync(i => i.ProductId == productId, tracked: true);
        
        foreach (var img in existingImages)
        {
            if (img.IsMain)
            {
                img.IsMain = false;
                _uow.ProductImages.Update(img);
            }
        }

        var newImage = new ProductImage
        {
            ProductId = productId,
            ImageUrl = imageUrl,
            PublicId = publicId,
            IsMain = true,
            DisplayOrder = 0
        };

        await _uow.ProductImages.AddAsync(newImage);
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
                Images      = v.Images?.Select(i => new ProductVariantImageVM
                {
                    Id = i.Id,
                    ImageUrl = i.ImageUrl,
                    PublicId = i.PublicId,
                    IsMain = i.IsMain
                }).ToList() ?? new List<ProductVariantImageVM>()
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

    private async Task UpdateProductStatusAsync(int productId)
    {
        var product = await _uow.Products.FindAsync(p => p.Id == productId, "Variants", ignoreQueryFilters: true);
        if (product != null)
        {
            if (!product.Variants.Any(v => v.Stock > 0 && v.IsActive))
            {
                if (product.IsActive)
                {
                    product.IsActive = false;
                    _uow.Products.Update(product);
                    await _uow.SaveAsync();
                }
            }
        }
    }
}
