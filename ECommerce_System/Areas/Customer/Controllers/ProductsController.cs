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
        const int PageSize = 12;
        page = Math.Max(1, page);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        sort = string.IsNullOrWhiteSpace(sort) ? "newest" : sort.Trim().ToLowerInvariant();

        var query = _unitOfWork.Products
            .Query()
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .Include(p => p.Reviews)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(p => p.Name.Contains(search));
        }

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        if (minPrice.HasValue)
        {
            query = query.Where(p =>
                (p.Variants.Where(v => v.IsActive && v.Stock > 0)
                    .Select(v => (decimal?)v.Price)
                    .OrderBy(price => price)
                    .FirstOrDefault() ?? p.BasePrice) >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            query = query.Where(p =>
                (p.Variants.Where(v => v.IsActive && v.Stock > 0)
                    .Select(v => (decimal?)v.Price)
                    .OrderBy(price => price)
                    .FirstOrDefault() ?? p.BasePrice) <= maxPrice.Value);
        }

        query = sort switch
        {
            "price_asc" => query.OrderBy(p =>
                p.Variants.Where(v => v.IsActive && v.Stock > 0)
                    .Select(v => (decimal?)v.Price)
                    .OrderBy(price => price)
                    .FirstOrDefault() ?? p.BasePrice),
            "price_desc" => query.OrderByDescending(p =>
                p.Variants.Where(v => v.IsActive && v.Stock > 0)
                    .Select(v => (decimal?)v.Price)
                    .OrderBy(price => price)
                    .FirstOrDefault() ?? p.BasePrice),
            "rating" => query.OrderByDescending(p => p.AverageRating),
            _ => query.OrderByDescending(p => p.CreatedAt)
        };

        var totalCount = await query.CountAsync();

        var products = await query
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var wishlistIds = new HashSet<int>();

        if (userId != null)
        {
            wishlistIds = (await _unitOfWork.WishlistItems
                .Query()
                .Where(w => w.UserId == userId)
                .Select(w => w.ProductId)
                .ToListAsync())
                .ToHashSet();
        }

        var categories = await _unitOfWork.Categories.GetAllAsync(tracked: false);
        var categorySelectList = categories.Select(c => new SelectListItem
        {
            Value = c.Id.ToString(),
            Text = c.Name
        });

        var vm = new ProductIndexCustomerVM
        {
            Products = products.Select(p => new ProductCardVM
            {
                Id = p.Id,
                Name = p.Name,
                BasePrice = p.BasePrice,
                MinVariantPrice = p.Variants.Where(v => v.IsActive && v.Stock > 0)
                    .Select(v => (decimal?)v.Price)
                    .OrderBy(price => price)
                    .FirstOrDefault(),
                AverageRating = p.AverageRating,
                ReviewCount = p.Reviews.Count(r => r.IsApproved && !r.IsRejected),
                MainImageUrl = p.Images.FirstOrDefault(i => i.IsMain)?.ImageUrl
                    ?? p.Images.OrderBy(i => i.DisplayOrder).FirstOrDefault()?.ImageUrl,
                CategoryName = p.Category?.Name,
                IsInWishlist = userId != null && wishlistIds.Contains(p.Id),
                HasStock = p.Variants.Any(v => v.IsActive && v.Stock > 0)
            }).ToList(),
            Categories = categorySelectList,
            CurrentPage = page,
            TotalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize)),
            TotalCount = totalCount,
            SearchQuery = search,
            SelectedCategoryId = categoryId,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            Sort = sort
        };

        return View(vm);
    }

    public async Task<IActionResult> Details(int id)
    {
        var product = await _unitOfWork.Products
            .Query()
            .Where(p => p.Id == id)
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .Include(p => p.Reviews)
            .FirstOrDefaultAsync();

        if (product == null || !product.IsActive)
        {
            return NotFound();
        }

        var reviews = product.Reviews
            .Where(r => r.IsApproved && !r.IsRejected)
            .OrderByDescending(r => r.CreatedAt)
            .ToList();

        var related = await _unitOfWork.Products
            .Query()
            .Where(p => p.CategoryId == product.CategoryId &&
                        p.Id != product.Id &&
                        p.IsActive)
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .OrderByDescending(p => p.CreatedAt)
            .Take(4)
            .ToListAsync();

        var vm = new ProductDetailsVM
        {
            Product = product,
            Reviews = reviews,
            RelatedProducts = related
        };

        return View(vm);
    }
}
