using ECommerce_System.Models;

namespace ECommerce_System.Repositories.IRepositories;

public interface ICategoryRepository : IRepository<Category>
{
    /// <summary>Gets all top-level categories (no parent) with their children.</summary>
    Task<IEnumerable<Category>> GetTopLevelWithChildrenAsync();
}
