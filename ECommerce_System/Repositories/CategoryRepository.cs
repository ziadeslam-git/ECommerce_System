using ECommerce_System.Data;
using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace ECommerce_System.Repositories;

public class CategoryRepository : Repository<Category>, ICategoryRepository
{
    public CategoryRepository(ApplicationDbContext context) : base(context) { }

    public async Task<IEnumerable<Category>> GetTopLevelWithChildrenAsync()
        => await _context.Categories
            .Where(c => c.ParentCategoryId == null)
            .Include(c => c.SubCategories)
            .OrderBy(c => c.Name)
            .ToListAsync();
}
