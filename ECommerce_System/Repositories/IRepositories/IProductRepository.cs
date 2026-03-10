using ECommerce_System.Models;

namespace ECommerce_System.Repositories.IRepositories;

public interface IProductRepository : IRepository<Product>
{
    /// <summary>Gets a product with its variants, images, and category.</summary>
    Task<Product?> GetWithDetailsAsync(int id);

    /// <summary>Gets all active products in a category (including sub-categories).</summary>
    Task<IEnumerable<Product>> GetByCategoryAsync(int categoryId);

    /// <summary>Recalculates and updates AverageRating from approved reviews.</summary>
    Task UpdateAverageRatingAsync(int productId);
}
