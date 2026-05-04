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

        var normalizedSize  = NormalizeSize(vm.Size);
        var normalizedColor = NormalizeColor(vm.Color);
        var normalizedSku   = NormalizeSku(vm.SKU);

        // Ensure (ProductId + Size + Color) is unique
        var duplicate = await _uow.ProductVariants
            .FindAsync(v => v.ProductId == vm.ProductId
                         && v.Size      == normalizedSize
                         && v.Color     == normalizedColor,
                         ignoreQueryFilters: true);

        if (duplicate is not null)
        {
            ModelState.AddModelError(string.Empty,
                $"A variant with {BuildVariantLabel(normalizedSize, normalizedColor)} already exists for this product.");
            ViewData["Title"]       = "Add Variant";
            ViewData["ProductName"] = vm.ProductName;
            return View(vm);
        }

        // Ensure SKU uniqueness
        var skuExists = await _uow.ProductVariants
            .FindAsync(v => v.SKU == normalizedSku, ignoreQueryFilters: true);
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
            Size      = normalizedSize,
            Color     = normalizedColor,
            SKU       = normalizedSku,
            Price     = vm.Price,
            Stock     = vm.Stock,
            IsActive  = vm.Stock > 0 ? vm.IsActive : false,
        };

        if (vm.ImageFiles is not null && vm.ImageFiles.Any())
        {
            var images = new List<ProductVariantImage>();
            var selectedMainNewIndex = ParseNewImageIndex(vm.SelectedMainImageKey);
            ProductVariantImage? mainImage = null;

            foreach (var (file, index) in vm.ImageFiles.Select((file, index) => (file, index)))
            {
                (string Url, string PublicId) res;
                try
                {
                    res = await _cloudinaryService.UploadAsync(file, "variants");
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                    ViewData["Title"] = "Add Variant";
                    ViewData["ProductName"] = vm.ProductName;
                    return View(vm);
                }

                var image = new ProductVariantImage
                {
                    ImageUrl = res.Url,
                    PublicId = res.PublicId,
                    IsMain   = selectedMainNewIndex.HasValue
                        ? selectedMainNewIndex.Value == index
                        : index == 0
                };

                if (image.IsMain)
                {
                    mainImage = image;
                }

                images.Add(image);
            }

            if (mainImage is null && images.Any())
            {
                mainImage = images[0];
                mainImage.IsMain = true;
            }

            if (vm.SetAsMainProductImage && mainImage is not null)
            {
                await SetVariantImageAsMainProductImage(variant.ProductId, mainImage.ImageUrl, mainImage.PublicId);
            }

            variant.Images = images;
        }

        await _uow.ProductVariants.AddAsync(variant);
        await _uow.SaveAsync();

        await UpdateProductStatusAsync(variant.ProductId);

        TempData["success"] = $"Variant {BuildVariantLabel(variant.Size, variant.Color)} added.";
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
            }).ToList() ?? new List<ProductVariantImageVM>(),
            SelectedMainImageKey = variant.Images?
                .FirstOrDefault(i => i.IsMain) is { } mainImage
                    ? $"existing:{mainImage.Id}"
                    : null
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ProductVariantVM vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateEditVmAsync(vm);
            ViewData["Title"]       = "Edit Variant";
            ViewData["ProductName"] = vm.ProductName;
            return View(vm);
        }

        var normalizedSize  = NormalizeSize(vm.Size);
        var normalizedColor = NormalizeColor(vm.Color);
        var normalizedSku   = NormalizeSku(vm.SKU);

        // FIX #7A: Include Images to prevent null issues when editing images
        var variant = await _uow.ProductVariants
            .FindAsync(v => v.Id == vm.Id, "Images", ignoreQueryFilters: true);
        if (variant is null) return NotFound();

        // Attach row version for optimistic concurrency
        if (vm.RowVersion is not null)
            _uow.SetRowVersion(variant, vm.RowVersion);

        // (Size + Color + ProductId) unique — exclude self
        var duplicate = await _uow.ProductVariants
            .FindAsync(v => v.ProductId == vm.ProductId
                         && v.Size      == normalizedSize
                         && v.Color     == normalizedColor
                         && v.Id        != vm.Id,
                         ignoreQueryFilters: true);
        if (duplicate is not null)
        {
            ModelState.AddModelError(string.Empty,
                $"A variant with {BuildVariantLabel(normalizedSize, normalizedColor)} already exists.");
            await PopulateEditVmAsync(vm);
            ViewData["Title"] = "Edit Variant";
            ViewData["ProductName"] = vm.ProductName;
            return View(vm);
        }

        // SKU unique — exclude self
        var skuConflict = await _uow.ProductVariants
            .FindAsync(v => v.SKU == normalizedSku && v.Id != vm.Id, ignoreQueryFilters: true);
        if (skuConflict is not null)
        {
            ModelState.AddModelError(nameof(vm.SKU), "This SKU is already used.");
            await PopulateEditVmAsync(vm);
            ViewData["Title"] = "Edit Variant";
            ViewData["ProductName"] = vm.ProductName;
            return View(vm);
        }

        try
        {
            variant.Size     = normalizedSize;
            variant.Color    = normalizedColor;
            variant.SKU      = normalizedSku;
            variant.Price    = vm.Price;
            variant.Stock    = vm.Stock;
            variant.IsActive = vm.Stock > 0 ? vm.IsActive : false;
            variant.Images ??= new List<ProductVariantImage>();

            var uploadedImages = new List<ProductVariantImage>();

            if (vm.ImageFiles is not null && vm.ImageFiles.Any())
            {
                foreach (var file in vm.ImageFiles)
                {
                    (string Url, string PublicId) res;
                    try
                    {
                        res = await _cloudinaryService.UploadAsync(file, "variants");
                    }
                    catch (InvalidOperationException ex)
                    {
                        ModelState.AddModelError(string.Empty, ex.Message);
                        await PopulateEditVmAsync(vm);
                        ViewData["Title"] = "Edit Variant";
                        ViewData["ProductName"] = vm.ProductName;
                        return View(vm);
                    }

                    var image = new ProductVariantImage
                    {
                        ImageUrl = res.Url,
                        PublicId = res.PublicId,
                        IsMain   = false
                    };

                    variant.Images.Add(image);
                    uploadedImages.Add(image);
                }
            }

            var resolvedMainImage = ResolveSelectedMainImage(variant.Images, uploadedImages, vm.SelectedMainImageKey);
            if (resolvedMainImage is not null)
            {
                foreach (var image in variant.Images)
                {
                    image.IsMain = ReferenceEquals(image, resolvedMainImage);
                }

                if (vm.SetAsMainProductImage)
                {
                    await SetVariantImageAsMainProductImage(
                        variant.ProductId,
                        resolvedMainImage.ImageUrl,
                        resolvedMainImage.PublicId);
                }
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

    // FIX #6: Always soft-delete — never physically remove variants
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var variant = await _uow.ProductVariants
            .GetByIdAsync(id, ignoreQueryFilters: true);
        if (variant is null) return NotFound();

        variant.IsActive = false;
        variant.Stock = 0;
        _uow.ProductVariants.Update(variant);
        await _uow.SaveAsync();

        TempData["success"] = "Variant deactivated successfully.";
        return RedirectToAction(nameof(Index), new { productId = variant.ProductId });
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

    // FIX #7B: Prevents duplicate ProductImage rows by matching on PublicId
    private async Task SetVariantImageAsMainProductImage(
        int productId, string imageUrl, string publicId)
    {
        var existing = await _uow.ProductImages
            .FindAllAsync(i => i.ProductId == productId, tracked: true);

        foreach (var img in existing)
        {
            img.IsMain = false;
            _uow.ProductImages.Update(img);
        }

        var match = existing.FirstOrDefault(i => i.PublicId == publicId);
        if (match is not null)
        {
            match.IsMain = true;
            _uow.ProductImages.Update(match);
        }
        else
        {
            await _uow.ProductImages.AddAsync(new ProductImage
            {
                ProductId = productId,
                ImageUrl = imageUrl,
                PublicId = publicId,
                IsMain = true,
                DisplayOrder = 0
            });
        }
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

    private async Task PopulateEditVmAsync(ProductVariantVM vm)
    {
        var variant = await _uow.ProductVariants
            .FindAsync(v => v.Id == vm.Id, "Product,Images", ignoreQueryFilters: true);

        if (variant is null) return;

        vm.ProductName = variant.Product.Name;
        vm.Images = variant.Images?.Select(i => new ProductVariantImageVM
        {
            Id = i.Id,
            ImageUrl = i.ImageUrl,
            PublicId = i.PublicId,
            IsMain = i.IsMain
        }).ToList() ?? new List<ProductVariantImageVM>();

        if (string.IsNullOrWhiteSpace(vm.SelectedMainImageKey))
        {
            var mainImage = variant.Images?.FirstOrDefault(i => i.IsMain) ?? variant.Images?.FirstOrDefault();
            vm.SelectedMainImageKey = mainImage is not null ? $"existing:{mainImage.Id}" : null;
        }
    }

    private static string NormalizeSize(string? size)
        => string.IsNullOrWhiteSpace(size) ? string.Empty : size.Trim().ToUpperInvariant();

    private static string NormalizeColor(string color)
        => color.Trim();

    private static string NormalizeSku(string sku)
        => sku.Trim().ToUpperInvariant();

    private static string BuildVariantLabel(string? size, string color)
        => string.IsNullOrWhiteSpace(size) ? $"Color {color} (No size)" : $"{size}/{color}";

    private static int? ParseNewImageIndex(string? selectedMainImageKey)
    {
        if (string.IsNullOrWhiteSpace(selectedMainImageKey) ||
            !selectedMainImageKey.StartsWith("new:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(selectedMainImageKey["new:".Length..], out var index) ? index : null;
    }

    private static ProductVariantImage? ResolveSelectedMainImage(
        IEnumerable<ProductVariantImage> allImages,
        IReadOnlyList<ProductVariantImage> uploadedImages,
        string? selectedMainImageKey)
    {
        var imageList = allImages.ToList();
        if (!imageList.Any()) return null;

        if (!string.IsNullOrWhiteSpace(selectedMainImageKey))
        {
            if (selectedMainImageKey.StartsWith("existing:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(selectedMainImageKey["existing:".Length..], out var existingId))
            {
                var existingImage = imageList.FirstOrDefault(i => i.Id == existingId);
                if (existingImage is not null) return existingImage;
            }

            if (selectedMainImageKey.StartsWith("new:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(selectedMainImageKey["new:".Length..], out var uploadedIndex) &&
                uploadedIndex >= 0 &&
                uploadedIndex < uploadedImages.Count)
            {
                return uploadedImages[uploadedIndex];
            }
        }

        return imageList.FirstOrDefault(i => i.IsMain) ?? imageList.FirstOrDefault();
    }
}
