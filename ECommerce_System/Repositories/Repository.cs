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

    public IQueryable<T> Query() => _dbSet;

    public async Task<IEnumerable<T>> GetAllAsync(string? includeProperties = null, bool tracked = true, bool ignoreQueryFilters = false)
    {
        IQueryable<T> query = _dbSet;
        if (!tracked)
            query = query.AsNoTracking();
        if (ignoreQueryFilters)
            query = query.IgnoreQueryFilters();
        query = ApplyIncludes(query, includeProperties);
        return await query.ToListAsync();
    }

    public async Task<T?> GetByIdAsync(int id, bool ignoreQueryFilters = false)
    {
        if (ignoreQueryFilters)
        {
            var keyProperty = _context.Model.FindEntityType(typeof(T))?.FindPrimaryKey()?.Properties.FirstOrDefault();
            if (keyProperty != null)
            {
                var parameter = Expression.Parameter(typeof(T), "e");
                var property  = Expression.Property(parameter, keyProperty.Name);
                if (keyProperty.ClrType == typeof(int))
                {
                    var equality = Expression.Equal(property, Expression.Constant(id));
                    var lambda   = Expression.Lambda<Func<T, bool>>(equality, parameter);
                    return await _dbSet.IgnoreQueryFilters().FirstOrDefaultAsync(lambda);
                }
            }
        }
        return await _dbSet.FindAsync(id);
    }

    public async Task<T?> FindAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, bool tracked = true, bool ignoreQueryFilters = false)
    {
        IQueryable<T> query = _dbSet;
        if (!tracked)
            query = query.AsNoTracking();
        if (ignoreQueryFilters)
            query = query.IgnoreQueryFilters();
        query = ApplyIncludes(query, includeProperties);
        return await query.FirstOrDefaultAsync(predicate);
    }

    public async Task<IEnumerable<T>> FindAllAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, bool tracked = true, bool ignoreQueryFilters = false)
    {
        IQueryable<T> query = _dbSet;
        if (!tracked)
            query = query.AsNoTracking();
        if (ignoreQueryFilters)
            query = query.IgnoreQueryFilters();
        query = ApplyIncludes(query, includeProperties);
        return await query.Where(predicate).ToListAsync();
    }

    /// <inheritdoc />
    public async Task<(IEnumerable<T> Items, int TotalCount)> GetPagedAsync(
        Expression<Func<T, bool>>? filter = null,
        string? includeProperties = null,
        int page = 1,
        int pageSize = 15,
        bool tracked = false,
        bool ignoreQueryFilters = false)
    {
        IQueryable<T> query = _dbSet;

        if (!tracked)
            query = query.AsNoTracking();

        if (ignoreQueryFilters)
            query = query.IgnoreQueryFilters();

        if (filter is not null)
            query = query.Where(filter);

        query = ApplyIncludes(query, includeProperties);

        // COUNT pushed to DB — no in-memory loading
        int totalCount = await query.CountAsync();

        // Apply a stable default OrderBy on the PK before Skip/Take to eliminate EF Core 10102
        // (Skip/Take without OrderBy produces non-deterministic results)
        query = ApplyDefaultOrderBy(query);

        // OFFSET/FETCH pushed to DB
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
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

        query = query.AsSplitQuery();

        foreach (var prop in includeProperties.Split(',', StringSplitOptions.RemoveEmptyEntries))
            query = query.Include(prop.Trim());

        return query;
    }

    // ─── Private helper: stable default OrderBy on PK to satisfy Skip/Take ───
    // Builds OrderBy(e => e.<PkProperty>) dynamically via Expression trees using EF model metadata.
    private IQueryable<T> ApplyDefaultOrderBy(IQueryable<T> query)
    {
        var entityType = _context.Model.FindEntityType(typeof(T));
        var pkProperty = entityType?.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (pkProperty == null) return query; // fallback: leave as-is

        var param      = Expression.Parameter(typeof(T), "e");
        var propAccess = Expression.Property(param, pkProperty.Name);
        var keySelector = Expression.Lambda(propAccess, param);

        var orderByMethod = typeof(Queryable)
            .GetMethods()
            .First(m => m.Name == "OrderBy" && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(T), pkProperty.ClrType);

        return (IQueryable<T>)orderByMethod.Invoke(null, [query, keySelector])!;
    }
}
