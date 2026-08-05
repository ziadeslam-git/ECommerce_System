using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.ViewModels.Customer;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerce_System.Areas.Customer.Controllers;

[Area("Customer")]
public class HomeController : Controller
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly RequestLocalizationOptions _localizationOptions;

    public HomeController(IUnitOfWork unitOfWork, IOptions<RequestLocalizationOptions> localizationOptions)
    {
        _unitOfWork = unitOfWork;
        _localizationOptions = localizationOptions.Value;
    }

    public async Task<IActionResult> Index()
    {
        // ── Featured Products: push OrderBy + Take(10) to DB via IQueryable ──
        var featuredProducts = await _unitOfWork.Products
            .Query()
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.CreatedAt)
            .Take(10)
            .Select(p => new ProductCardVM
            {
                Id       = p.Id,
                Name     = p.Name,
                BasePrice = p.BasePrice,
                MinVariantPrice = (p.Variants
                    .Where(v => v.IsActive && v.Stock > 0)
                    .OrderBy(v => v.Price)
                    .Select(v => (decimal?)v.Price)
                    .FirstOrDefault()) ?? p.BasePrice,
                DefaultVariantId = p.Variants
                    .Where(v => v.IsActive && v.Stock > 0)
                    .OrderBy(v => v.Price)
                    .Select(v => (int?)v.Id)
                    .FirstOrDefault(),
                AverageRating = p.AverageRating,
                MainImageUrl  = p.Images
                    .Where(i => i.IsMain)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault()
                    ?? p.Images
                        .OrderBy(i => i.DisplayOrder)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault(),
                CategoryName = p.Category != null ? p.Category.Name : null
            })
            .ToListAsync();

        // ── Featured Gift Bundle: load only the single needed bundle from DB ──
        var featuredGiftBundle = await _unitOfWork.GiftBundles
            .Query()
            .AsNoTracking()
            .Where(gb => gb.IsActive && gb.Items.Count >= 2)
            .OrderByDescending(gb => gb.IsFeatured)
            .ThenByDescending(gb => gb.UpdatedAt)
            .Include(gb => gb.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p.Images)
            .Include(gb => gb.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p.Variants)
            .FirstOrDefaultAsync();

        // ── Categories: project Count at DB level — no full Product rows loaded ──
        var categories = await _unitOfWork.Categories
            .Query()
            .AsNoTracking()
            .Where(c => c.Products.Any(p => p.IsActive))
            .OrderBy(c => c.Name)
            .Select(c => new CategoryCardVM
            {
                Id           = c.Id,
                Name         = c.Name,
                Slug         = c.Slug,
                ProductCount = c.Products.Count(p => p.IsActive)
            })
            .ToListAsync();

        var vm = new HomeIndexVM
        {
            FeaturedProducts = featuredProducts,
            FeaturedGiftBundle = featuredGiftBundle == null
                ? null
                : new GiftBundleHomeVM
                {
                    Id = featuredGiftBundle.Id,
                    Name = featuredGiftBundle.Name,
                    Description = string.IsNullOrWhiteSpace(featuredGiftBundle.Description)
                        ? "Complete the look with a curated offer built from standout products."
                        : featuredGiftBundle.Description!,
                    BundlePrice   = featuredGiftBundle.BundlePrice,
                    OriginalTotal = featuredGiftBundle.Items.Sum(item => ResolveBundleDisplayPrice(item.Product)),
                    Items = featuredGiftBundle.Items
                        .OrderBy(item => item.SortOrder)
                        .Select(item => new GiftBundleHomeItemVM
                        {
                            ProductId   = item.ProductId,
                            ProductName = item.Product.Name,
                            MainImageUrl = ResolveProductImage(item.Product)
                        })
                        .ToList()
                },
            Categories = categories
        };

        return View(vm);
    }

    private static string? ResolveProductImage(Product product)
    {
        return product.Images.FirstOrDefault(i => i.IsMain)?.ImageUrl
            ?? product.Images.OrderBy(i => i.DisplayOrder).FirstOrDefault()?.ImageUrl
            ?? product.Variants
                .SelectMany(v => v.Images)
                .OrderByDescending(i => i.IsMain)
                .ThenBy(i => i.Id)
                .FirstOrDefault()?.ImageUrl;
    }

    private static decimal ResolveBundleDisplayPrice(Product product)
    {
        var variantPrice = product.Variants
            .Where(v => v.IsActive && v.Stock > 0)
            .OrderBy(v => v.Price)
            .Select(v => v.Price)
            .FirstOrDefault();

        return variantPrice > 0 ? variantPrice : product.BasePrice;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetLanguage(string? culture, string? returnUrl)
    {
        var supportedCultures = _localizationOptions.SupportedUICultures?
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        if (string.IsNullOrWhiteSpace(culture) || !supportedCultures.Contains(culture))
        {
            culture = _localizationOptions.DefaultRequestCulture.UICulture.Name;
        }

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) });

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction(nameof(Index), "Home", new { area = "Customer" });
    }

    [HttpGet]
    public IActionResult Error() => View("~/Views/Shared/Error.cshtml");
}

