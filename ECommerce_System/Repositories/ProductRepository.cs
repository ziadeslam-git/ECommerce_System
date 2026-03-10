using ECommerce_System.Data;
using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace ECommerce_System.Repositories;

public class ProductRepository : Repository<Product>, IProductRepository
{
    public ProductRepository(ApplicationDbContext context) : base(context) { }

    public async Task<Product?> GetWithDetailsAsync(int id)
        => await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id);

    public async Task<IEnumerable<Product>> GetByCategoryAsync(int categoryId)
        => await _context.Products
            .Where(p => p.CategoryId == categoryId && p.IsActive)
            .Include(p => p.Images.Where(i => i.IsMain))
            .Include(p => p.Category)
            .ToListAsync();

    public async Task UpdateAverageRatingAsync(int productId)
    {
        var avg = await _context.Reviews
            .Where(r => r.ProductId == productId && r.IsApproved)
            .AverageAsync(r => (double?)r.Rating) ?? 0;

        var product = await _context.Products.FindAsync(productId);
        if (product is not null)
        {
            product.AverageRating = Math.Round(avg, 1);
            product.UpdatedAt = DateTime.UtcNow;
        }
    }
}
