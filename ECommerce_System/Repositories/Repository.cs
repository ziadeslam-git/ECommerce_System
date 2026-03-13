using ECommerce_System.Data;
using ECommerce_System.Repositories.IRepositories;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace ECommerce_System.Repositories;

public class Repository<T> : IRepository<T> where T : class
{
    protected readonly ApplicationDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public Repository(ApplicationDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public async Task<IEnumerable<T>> GetAllAsync(string? includeProperties = null, bool tracked = true)
    {
        IQueryable<T> query = _dbSet;
        if (!tracked)
        {
            query = query.AsNoTracking();
        }
        query = ApplyIncludes(query, includeProperties);
        return await query.ToListAsync();
    }

    public async Task<T?> GetByIdAsync(int id)
        => await _dbSet.FindAsync(id);

    public async Task<T?> FindAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null)
    {
        IQueryable<T> query = _dbSet;
        query = ApplyIncludes(query, includeProperties);
        return await query.FirstOrDefaultAsync(predicate);
    }

    public async Task<IEnumerable<T>> FindAllAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, bool tracked = true)
    {
        IQueryable<T> query = _dbSet;
        if (!tracked)
        {
            query = query.AsNoTracking();
        }
        query = ApplyIncludes(query, includeProperties);
        return await query.Where(predicate).ToListAsync();
    }

    public async Task AddAsync(T entity)
        => await _dbSet.AddAsync(entity);

    public void Update(T entity)
        => _dbSet.Update(entity);

    public void Remove(T entity)
        => _dbSet.Remove(entity);

    public void RemoveRange(IEnumerable<T> entities)
        => _dbSet.RemoveRange(entities);

    // ─── Private helper: comma-separated include properties ───
    private static IQueryable<T> ApplyIncludes(IQueryable<T> query, string? includeProperties)
    {
        if (string.IsNullOrWhiteSpace(includeProperties))
            return query;

        foreach (var prop in includeProperties.Split(',', StringSplitOptions.RemoveEmptyEntries))
            query = query.Include(prop.Trim());

        return query;
    }
}
