using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.ViewModels.Customer;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
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
        var featuredProducts = await _unitOfWork.Products
            .FindAllAsync(p => p.IsActive, "Images,Category,Variants", tracked: false);

        var categories = await _unitOfWork.Categories
            .FindAllAsync(c => c.Products.Any(p => p.IsActive), "Products", tracked: false);

        var vm = new HomeIndexVM
        {
            FeaturedProducts = featuredProducts
                .OrderByDescending(p => p.CreatedAt)
                .Take(10)
                .Select(p => new ProductCardVM
                {
                    Id = p.Id,
                    Name = p.Name,
                    BasePrice = p.BasePrice,
                    MinVariantPrice = p.Variants.Where(v => v.IsActive && v.Stock > 0)
                        .Select(v => v.Price)
                        .OrderBy(price => price)
                        .FirstOrDefault(),
                    DefaultVariantId = p.Variants
                        .Where(v => v.IsActive && v.Stock > 0)
                        .OrderBy(v => v.Price)
                        .Select(v => (int?)v.Id)
                        .FirstOrDefault(),
                    AverageRating = p.AverageRating,
                    MainImageUrl = p.Images.FirstOrDefault(i => i.IsMain)?.ImageUrl
                        ?? p.Images.OrderBy(i => i.DisplayOrder).FirstOrDefault()?.ImageUrl,
                    CategoryName = p.Category?.Name
                }).ToList(),
            Categories = categories
                .OrderBy(c => c.Name)
                .Select(c => new CategoryCardVM
                {
                    Id = c.Id,
                    Name = c.Name,
                    Slug = c.Slug,
                    ProductCount = c.Products.Count(p => p.IsActive)
                }).ToList()
        };

        return View(vm);
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

