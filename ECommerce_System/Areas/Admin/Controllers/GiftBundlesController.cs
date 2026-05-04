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
public class GiftBundlesController : Controller
{
    private readonly IUnitOfWork _unitOfWork;

    public GiftBundlesController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Gift Bundles";

        var bundles = await _unitOfWork.GiftBundles
            .GetAllAsync("Items.Product.Images,Items.Product.Variants", tracked: false);

        var vm = bundles
            .OrderByDescending(bundle => bundle.IsFeatured)
            .ThenByDescending(bundle => bundle.UpdatedAt)
            .Select(bundle => new GiftBundleIndexVM
            {
                Id = bundle.Id,
                Name = bundle.Name,
                Description = bundle.Description,
                BundlePrice = bundle.BundlePrice,
                OriginalTotal = bundle.Items.Sum(item => ResolveProductBundlePrice(item.Product)),
                IsActive = bundle.IsActive,
                IsFeatured = bundle.IsFeatured,
                ProductCount = bundle.Items.Count,
                Products = bundle.Items
                    .OrderBy(item => item.SortOrder)
                    .Select(item => new GiftBundleProductPreviewVM
                    {
                        ProductId = item.ProductId,
                        ProductName = item.Product.Name,
                        ImageUrl = item.Product.Images.FirstOrDefault(i => i.IsMain)?.ImageUrl
                            ?? item.Product.Images.OrderBy(i => i.DisplayOrder).FirstOrDefault()?.ImageUrl
                    })
                    .ToList()
            })
            .ToList();

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        ViewData["Title"] = "Create Gift Bundle";
        var vm = new GiftBundleFormVM
        {
            IsActive = true
        };

        await PopulateProductSelectionAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GiftBundleFormVM vm)
    {
        await ValidateAndLoadSelectedProductsAsync(vm);

        if (!ModelState.IsValid)
        {
            await PopulateProductSelectionAsync(vm);
            ViewData["Title"] = "Create Gift Bundle";
            return View(vm);
        }

        if (vm.IsFeatured)
        {
            await ClearFeaturedFlagFromOtherBundlesAsync();
        }

        var bundle = new GiftBundle
        {
            Name = vm.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim(),
            BundlePrice = vm.BundlePrice,
            IsActive = vm.IsActive,
            IsFeatured = vm.IsFeatured,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.GiftBundles.AddAsync(bundle);
        await _unitOfWork.SaveAsync();

        for (var index = 0; index < vm.SelectedProductIds.Count; index++)
        {
            await _unitOfWork.GiftBundleProducts.AddAsync(new GiftBundleProduct
            {
                GiftBundleId = bundle.Id,
                ProductId = vm.SelectedProductIds[index],
                SortOrder = index
            });
        }

        await _unitOfWork.SaveAsync();

        TempData["success"] = "Gift bundle created successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var bundle = await _unitOfWork.GiftBundles
            .FindAsync(gb => gb.Id == id, "Items", tracked: false);

        if (bundle == null)
        {
            return NotFound();
        }

        var vm = new GiftBundleFormVM
        {
            Id = bundle.Id,
            Name = bundle.Name,
            Description = bundle.Description,
            BundlePrice = bundle.BundlePrice,
            IsActive = bundle.IsActive,
            IsFeatured = bundle.IsFeatured,
            SelectedProductIds = bundle.Items
                .OrderBy(item => item.SortOrder)
                .Select(item => item.ProductId)
                .ToList()
        };

        await PopulateProductSelectionAsync(vm);
        ViewData["Title"] = "Edit Gift Bundle";
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, GiftBundleFormVM vm)
    {
        if (id != vm.Id)
        {
            return BadRequest();
        }

        var bundle = await _unitOfWork.GiftBundles.GetByIdAsync(id);
        if (bundle == null)
        {
            return NotFound();
        }

        await ValidateAndLoadSelectedProductsAsync(vm);

        if (!ModelState.IsValid)
        {
            await PopulateProductSelectionAsync(vm);
            ViewData["Title"] = "Edit Gift Bundle";
            return View(vm);
        }

        if (vm.IsFeatured)
        {
            await ClearFeaturedFlagFromOtherBundlesAsync(id);
        }

        bundle.Name = vm.Name.Trim();
        bundle.Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim();
        bundle.BundlePrice = vm.BundlePrice;
        bundle.IsActive = vm.IsActive;
        bundle.IsFeatured = vm.IsFeatured;
        bundle.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.GiftBundles.Update(bundle);

        var existingItems = (await _unitOfWork.GiftBundleProducts
            .FindAllAsync(item => item.GiftBundleId == id))
            .ToList();

        if (existingItems.Count > 0)
        {
            _unitOfWork.GiftBundleProducts.RemoveRange(existingItems);
        }

        for (var index = 0; index < vm.SelectedProductIds.Count; index++)
        {
            await _unitOfWork.GiftBundleProducts.AddAsync(new GiftBundleProduct
            {
                GiftBundleId = bundle.Id,
                ProductId = vm.SelectedProductIds[index],
                SortOrder = index
            });
        }

        await _unitOfWork.SaveAsync();

        TempData["success"] = "Gift bundle updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var bundle = await _unitOfWork.GiftBundles.GetByIdAsync(id);
        if (bundle == null)
        {
            return NotFound();
        }

        bundle.IsActive = !bundle.IsActive;
        bundle.UpdatedAt = DateTime.UtcNow;

        if (!bundle.IsActive && bundle.IsFeatured)
        {
            bundle.IsFeatured = false;
        }

        _unitOfWork.GiftBundles.Update(bundle);
        await _unitOfWork.SaveAsync();

        TempData["success"] = $"Gift bundle '{bundle.Name}' is now {(bundle.IsActive ? "active" : "inactive")}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetFeatured(int id)
    {
        var bundle = await _unitOfWork.GiftBundles.GetByIdAsync(id);
        if (bundle == null)
        {
            return NotFound();
        }

        await ClearFeaturedFlagFromOtherBundlesAsync(id);
        bundle.IsFeatured = true;
        bundle.IsActive = true;
        bundle.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.GiftBundles.Update(bundle);
        await _unitOfWork.SaveAsync();

        TempData["success"] = $"Gift bundle '{bundle.Name}' is now featured on the home page.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<Product>> ValidateAndLoadSelectedProductsAsync(GiftBundleFormVM vm)
    {
        vm.SelectedProductIds = vm.SelectedProductIds
            .Distinct()
            .ToList();

        if (vm.SelectedProductIds.Count < 2)
        {
            ModelState.AddModelError(nameof(vm.SelectedProductIds), "Select at least 2 products for the bundle.");
        }

        var products = (await _unitOfWork.Products
            .FindAllAsync(p => vm.SelectedProductIds.Contains(p.Id), "Images,Category,Variants", tracked: false))
            .ToList();

        if (products.Count != vm.SelectedProductIds.Count)
        {
            ModelState.AddModelError(nameof(vm.SelectedProductIds), "One or more selected products are no longer available.");
        }

        var unavailableProducts = products
            .Where(product => !HasAvailableVariant(product))
            .Select(product => product.Name)
            .ToList();

        if (unavailableProducts.Count > 0)
        {
            ModelState.AddModelError(nameof(vm.SelectedProductIds),
                $"These products cannot join a bundle because they have no active in-stock variant: {string.Join(", ", unavailableProducts)}.");
        }

        vm.OriginalTotal = products
            .Where(product => vm.SelectedProductIds.Contains(product.Id))
            .Sum(ResolveProductBundlePrice);

        if (vm.OriginalTotal > 0 && vm.BundlePrice >= vm.OriginalTotal)
        {
            ModelState.AddModelError(nameof(vm.BundlePrice), "Offer price should be lower than the combined original price.");
        }

        return products;
    }

    private async Task PopulateProductSelectionAsync(GiftBundleFormVM vm)
    {
        var selectedIds = vm.SelectedProductIds
            .Distinct()
            .ToHashSet();

        var products = (await _unitOfWork.Products
            .FindAllAsync(p => p.IsActive, "Images,Category,Variants", tracked: false))
            .OrderBy(product => product.Name)
            .ToList();

        vm.ProductCards = products
            .Select(product => new GiftBundleProductPickerVM
            {
                ProductId = product.Id,
                ProductName = product.Name,
                CategoryName = product.Category?.Name ?? "Uncategorized",
                ImageUrl = product.Images.FirstOrDefault(i => i.IsMain)?.ImageUrl
                    ?? product.Images.OrderBy(i => i.DisplayOrder).FirstOrDefault()?.ImageUrl,
                Price = ResolveProductBundlePrice(product),
                HasAvailableVariant = HasAvailableVariant(product),
                Selected = selectedIds.Contains(product.Id)
            })
            .ToList();

        vm.ProductOptions = vm.ProductCards
            .Select(product => new SelectListItem(product.ProductName, product.ProductId.ToString(), product.Selected))
            .ToList();

        vm.OriginalTotal = vm.ProductCards
            .Where(product => product.Selected)
            .Sum(product => product.Price);
    }

    private async Task ClearFeaturedFlagFromOtherBundlesAsync(int? excludedBundleId = null)
    {
        var featuredBundles = await _unitOfWork.GiftBundles
            .FindAllAsync(bundle => bundle.IsFeatured && (!excludedBundleId.HasValue || bundle.Id != excludedBundleId.Value));

        foreach (var giftBundle in featuredBundles)
        {
            giftBundle.IsFeatured = false;
            giftBundle.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.GiftBundles.Update(giftBundle);
        }
    }

    private static decimal ResolveProductBundlePrice(Product product)
    {
        var variantPrice = product.Variants
            .Where(v => v.IsActive && v.Stock > 0)
            .OrderBy(v => v.Price)
            .Select(v => v.Price)
            .FirstOrDefault();

        return variantPrice > 0 ? variantPrice : product.BasePrice;
    }

    private static bool HasAvailableVariant(Product product)
    {
        return product.Variants.Any(v => v.IsActive && v.Stock > 0);
    }
}
