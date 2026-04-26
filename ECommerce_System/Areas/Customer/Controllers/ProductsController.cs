using System.Security.Claims;
using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.ViewModels.Customer.ProductController;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ECommerce_System.Areas.Customer.Controllers;

[Area("Customer")]
public class ProductsController : Controller
{
    private readonly IUnitOfWork _unitOfWork;

    public ProductsController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IActionResult> Index(
        string? search,
        int? categoryId,
        decimal? minPrice,
        decimal? maxPrice,
        string? sort,
        int page = 1)
    {
        const int pageSize = 12;

        var query = _unitOfWork.Products
            .Query()
            .Where(p => p.IsActive)
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
            query = query.Where(p => p.Name.Contains(search));

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId);

        if (minPrice.HasValue)
            query = query.Where(p => p.BasePrice >= minPrice);

        if (maxPrice.HasValue)
            query = query.Where(p => p.BasePrice <= maxPrice);

        query = sort switch
        {
            "price_asc"   => query.OrderBy(p => p.BasePrice),
            "price_desc"  => query.OrderByDescending(p => p.BasePrice),
            "rating"      => query.OrderByDescending(p => p.AverageRating),
            _             => query.OrderByDescending(p => p.CreatedAt)
        };

        var totalCount = await query.CountAsync();

        var products = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Get wishlist for current user
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var wishlistIds = new List<int>();

        if (userId != null)
        {
            wishlistIds = await _unitOfWork.WishlistItems
                .Query()
                .Where(w => w.UserId == userId)
                .Select(w => w.ProductId)
                .ToListAsync();
        }

        // Build category dropdown
        var categories = await _unitOfWork.Categories.GetAllAsync();
        var categorySelectList = categories.Select(c => new SelectListItem
        {
            Value = c.Id.ToString(),
            Text  = c.Name
        });

        var vm = new ProductIndexCustomerVM
        {
            Products = products.Select(p => new ProductCardVM
            {
                Id             = p.Id,
                Name           = p.Name,
                BasePrice      = p.BasePrice,
                MinVariantPrice = p.Variants.Where(v => v.IsActive && v.Stock > 0)
                                            .Select(v => v.Price)
                                            .OrderBy(price => price)
                                            .FirstOrDefault() is decimal vp && vp > 0 ? vp : (decimal?)null,
                AverageRating  = p.AverageRating,
                MainImageUrl   = p.Images.FirstOrDefault(i => i.IsMain)?.ImageUrl
                              ?? p.Images.OrderBy(i => i.DisplayOrder).FirstOrDefault()?.ImageUrl,
                CategoryName   = p.Category?.Name,
                IsInWishlist   = wishlistIds.Contains(p.Id),
                HasStock       = p.Variants.Any(v => v.IsActive && v.Stock > 0)
            }).ToList(),

            CurrentPage        = page,
            TotalPages         = (int)Math.Ceiling(totalCount / (double)pageSize),
            TotalCount         = totalCount,
            Categories         = categorySelectList,
            SearchQuery        = search,
            SelectedCategoryId = categoryId,
            MinPrice           = minPrice,
            MaxPrice           = maxPrice,
            Sort               = sort
        };

        return View(vm);
    }

    public async Task<IActionResult> Details(int id)
    {
        var product = await _unitOfWork.Products
            .Query()
            .Where(p => p.Id == id && p.IsActive)
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .Include(p => p.Reviews)
            .FirstOrDefaultAsync();

        if (product == null)
            return NotFound();

        // Approved Reviews only
        var reviews = product.Reviews
            .Where(r => r.IsApproved)
            .ToList();

        // Related products
        var related = await _unitOfWork.Products
            .Query()
            .Where(p => p.CategoryId == product.CategoryId &&
                        p.Id != product.Id &&
                        p.IsActive)
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .Take(4)
            .ToListAsync();

        var vm = new ProductDetailsVM
        {
            Product        = product,
            Reviews        = reviews,
            RelatedProducts = related
        };

        return View(vm);
    }
}
