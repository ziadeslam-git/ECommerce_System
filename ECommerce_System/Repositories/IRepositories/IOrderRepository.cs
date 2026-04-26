using ECommerce_System.Models;

namespace ECommerce_System.Repositories.IRepositories;

public interface IOrderRepository : IRepository<Order>
{
    /// <summary>Gets an order with all related data (items, payment, shipment, address).</summary>
    Task<Order?> GetOrderWithDetailsAsync(int orderId);

    /// <summary>Gets all orders for a specific user, including first product image for thumbnail.</summary>
    Task<IEnumerable<Order>> GetOrdersByUserAsync(string userId);

    /// <summary>Gets a paginated list of orders for a specific user.</summary>
    Task<(IEnumerable<Order> Orders, int TotalCount)> GetOrdersByUserPagedAsync(
        string userId, int page = 1, int pageSize = 10);
}
