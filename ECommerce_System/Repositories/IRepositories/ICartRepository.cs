using ECommerce_System.Models;

namespace ECommerce_System.Repositories.IRepositories;

public interface ICartRepository : IRepository<Cart>
{
    /// <summary>Gets a user's cart with all items and their product variant details.</summary>
    Task<Cart?> GetCartByUserIdAsync(string userId);
}
