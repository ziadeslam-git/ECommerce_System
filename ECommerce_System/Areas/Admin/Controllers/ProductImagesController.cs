using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce_System.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SD.Role_Admin)]
public class ProductImagesController : Controller
{
    private readonly IUnitOfWork _uow;
    private readonly ICloudinaryService _cloudinary;

    public ProductImagesController(IUnitOfWork uow, ICloudinaryService cloudinary)
    {
        _uow = uow;
        _cloudinary = cloudinary;
    }

    // ─── INDEX — image gallery for a product ─────────────────────────────────

    public async Task<IActionResult> Index(int productId)
    {
        var product = await _uow.Products
            .FindAsync(p => p.Id == productId, "Category,Images,Variants", ignoreQueryFilters: true);

        if (product is null) return NotFound();

        ViewData["Title"]       = $"Images — {product.Name}";
        ViewData["ProductId"]   = productId;
        ViewData["ProductName"] = product.Name;

        var vm = MapToDetailsVM(product);
        return View(vm);
    }

    // ─── UPLOAD (POST) ────────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(UploadImageVM vm)
    {
        if (vm.ImageFile is null || vm.ImageFile.Length == 0)
        {
            TempData["error"] = "Please select a valid image file.";
            return RedirectToAction(nameof(Index), new { productId = vm.ProductId });
        }

        try
        {
            var (url, publicId) = await _cloudinary.UploadAsync(
                vm.ImageFile, SD.Cloudinary_ProductFolder);

            // If this is the first image or IsMain was checked, clear existing mains first
            var existingImages = await _uow.ProductImages
                .FindAllAsync(i => i.ProductId == vm.ProductId);
            var imageList = existingImages.ToList();

            bool setAsMain = vm.IsMain || !imageList.Any();

            if (setAsMain)
            {
                foreach (var img in imageList.Where(i => i.IsMain))
                {
                    img.IsMain = false;
                    _uow.ProductImages.Update(img);
                }
            }

            var newImage = new ProductImage
            {
                ProductId    = vm.ProductId,
                ImageUrl     = url,
                PublicId     = publicId,
                IsMain       = setAsMain,
                DisplayOrder = vm.DisplayOrder > 0 ? vm.DisplayOrder : imageList.Count + 1,
            };

            await _uow.ProductImages.AddAsync(newImage);
            await _uow.SaveAsync();

            TempData["success"] = "Image uploaded successfully.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["error"] = ex.Message;
        }
        catch (Exception ex)
        {
            TempData["error"] = $"Upload failed: {ex.Message}";
        }

        return RedirectToAction(nameof(Index), new { productId = vm.ProductId });
    }

    // ─── SET MAIN ─────────────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetMain(int id)
    {
        var image = await _uow.ProductImages.GetByIdAsync(id);
        if (image is null) return NotFound();

        // Clear previous main
        var siblings = await _uow.ProductImages
            .FindAllAsync(i => i.ProductId == image.ProductId && i.IsMain);
        foreach (var s in siblings)
        {
            s.IsMain = false;
            _uow.ProductImages.Update(s);
        }

        image.IsMain = true;
        _uow.ProductImages.Update(image);
        await _uow.SaveAsync();

        TempData["success"] = "Main image updated.";
        return RedirectToAction(nameof(Index), new { productId = image.ProductId });
    }

    // ─── DELETE ───────────────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var image = await _uow.ProductImages.GetByIdAsync(id);
        if (image is null) return NotFound();

        int productId = image.ProductId;
        bool wasMain  = image.IsMain;

        try
        {
            await _cloudinary.DeleteAsync(image.PublicId);
        }
        catch
        {
            // Non-fatal — still remove the DB record
        }

        _uow.ProductImages.Remove(image);
        await _uow.SaveAsync();

        // If deleted image was main, promote the next one
        if (wasMain)
        {
            var next = await _uow.ProductImages
                .FindAsync(i => i.ProductId == productId);
            if (next is not null)
            {
                next.IsMain = true;
                _uow.ProductImages.Update(next);
                await _uow.SaveAsync();
            }
        }

        TempData["success"] = "Image deleted.";
        return RedirectToAction(nameof(Index), new { productId });
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
