using ECommerce_System.Repositories;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.ViewModels.Admin;

namespace ECommerce_System.Areas.Customer.Controllers
{
    
    
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
                    .Include(p => p.Variants);

                
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(p => p.Name.Contains(search));

                if (categoryId.HasValue)
                    query = query.Where(p => p.CategoryId == categoryId);

                if (minPrice.HasValue)
                    query = query.Where(p => p.Price >= minPrice);

                if (maxPrice.HasValue)
                    query = query.Where(p => p.Price <= maxPrice);

                
                query = sort switch
                {
                    "price_asc" => query.OrderBy(p => p.Price),
                    "price_desc" => query.OrderByDescending(p => p.Price),
                    "rating" => query.OrderByDescending(p => p.Rating),
                    _ => query.OrderByDescending(p => p.CreatedAt) 
                };

                
                var totalCount = await query.CountAsync();

                var products = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                
                var userId = User.GetUserId(); 
                var wishlistIds = new List<int>();

                if (userId != null)
                {
                    wishlistIds = await _unitOfWork.Wishlist
                        .Query()
                        .Where(w => w.UserId == userId)
                        .Select(w => w.ProductId)
                        .ToListAsync();
                }

                var vm = new ProductIndexCustomerVM
                {
                    Products = products.Select(p => new ProductCardVM
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Price = p.Price,
                        Image = p.Images.FirstOrDefault()?.Url,
                        IsInWishlist = wishlistIds.Contains(p.Id)
                    }).ToList(),

                    CurrentPage = page,
                    TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                    Categories = await _unitOfWork.Categories.GetAllAsync()
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

            // Related المنتجات
            var related = await _unitOfWork.Products
                .Query()
                .Where(p => p.CategoryId == product.CategoryId &&
                            p.Id != product.Id &&
                            p.IsActive)
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
}

