using ECommerce_System.Data;
using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace ECommerce_System.Repositories;

public class OrderRepository : Repository<Order>, IOrderRepository
{
    public OrderRepository(ApplicationDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<Order?> GetOrderWithDetailsAsync(int orderId)
        => await _context.Orders
            .AsSplitQuery()
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.ProductVariant)
                    .ThenInclude(v => v.Product)
                        .ThenInclude(p => p.Images.Where(i => i.IsMain))
            .Include(o => o.Address)
            .Include(o => o.Payment)
            .Include(o => o.Shipment)
            .FirstOrDefaultAsync(o => o.Id == orderId);

    /// <inheritdoc />
    public async Task<IEnumerable<Order>> GetOrdersByUserAsync(string userId)
        => await _context.Orders
            .Where(o => o.UserId == userId)
            .AsSplitQuery()
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.ProductVariant)
                    .ThenInclude(v => v.Product)
                        .ThenInclude(p => p.Images.Where(i => i.IsMain))
            .Include(o => o.Shipment)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

    /// <inheritdoc />
    public async Task<(IEnumerable<Order> Orders, int TotalCount)> GetOrdersByUserPagedAsync(
        string userId, int page = 1, int pageSize = 10)
    {
        var baseQuery = _context.Orders
            .Where(o => o.UserId == userId);

        var totalCount = await baseQuery.CountAsync();

        // ── Includes MUST come before Skip/Take so EF Core paginates on orders,
        //    not on joined rows. AsSplitQuery keeps the includes efficient.
        var orders = await baseQuery
            .OrderByDescending(o => o.CreatedAt)
            .AsSplitQuery()
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.ProductVariant)
                    .ThenInclude(v => v.Product)
                        .ThenInclude(p => p.Images.Where(i => i.IsMain))
            .Include(o => o.Shipment)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (orders, totalCount);
    }
}
