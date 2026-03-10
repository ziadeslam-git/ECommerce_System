using ECommerce_System.Data;
using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace ECommerce_System.Repositories;

public class OrderRepository : Repository<Order>, IOrderRepository
{
    public OrderRepository(ApplicationDbContext context) : base(context) { }

    public async Task<Order?> GetOrderWithDetailsAsync(int orderId)
        => await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.ProductVariant)
                    .ThenInclude(v => v.Product)
                        .ThenInclude(p => p.Images.Where(i => i.IsMain))
            .Include(o => o.Address)
            .Include(o => o.Payment)
            .Include(o => o.Shipment)
            .FirstOrDefaultAsync(o => o.Id == orderId);

    public async Task<IEnumerable<Order>> GetOrdersByUserAsync(string userId)
        => await _context.Orders
            .Where(o => o.UserId == userId)
            .Include(o => o.OrderItems)
            .Include(o => o.Shipment)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
}
