using ECommerce_System.Data;
using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace ECommerce_System.Repositories;

public class CartRepository : Repository<Cart>, ICartRepository
{
    public CartRepository(ApplicationDbContext context) : base(context) { }

    public async Task<Cart?> GetCartByUserIdAsync(string userId)
        => await _context.Carts
            .Include(c => c.Items)
                .ThenInclude(ci => ci.ProductVariant)
                    .ThenInclude(v => v.Product)
                        .ThenInclude(p => p.Images.Where(i => i.IsMain))
            .FirstOrDefaultAsync(c => c.UserId == userId);
}
