using System.Linq.Expressions;

namespace ECommerce_System.Repositories.IRepositories;

public interface IRepository<T> where T : class
{
    /// <summary>Returns all entities, optionally including related data.</summary>
    Task<IEnumerable<T>> GetAllAsync(string? includeProperties = null, bool tracked = true, bool ignoreQueryFilters = false);

    /// <summary>Returns a single entity by primary key.</summary>
    Task<T?> GetByIdAsync(int id, bool ignoreQueryFilters = false);

    /// <summary>Returns the first entity matching the predicate, optionally with includes.</summary>
    Task<T?> FindAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, bool tracked = true, bool ignoreQueryFilters = false);

    /// <summary>Returns all entities matching the predicate.</summary>
    Task<IEnumerable<T>> FindAllAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, bool tracked = true, bool ignoreQueryFilters = false);

    /// <summary>Adds a new entity to the context (does not save).</summary>
    Task AddAsync(T entity);

    /// <summary>Marks entity as modified (does not save).</summary>
    void Update(T entity);

    /// <summary>Marks entity for deletion (does not save).</summary>
    void Remove(T entity);

    /// <summary>Marks a range of entities for deletion (does not save).</summary>
    void RemoveRange(IEnumerable<T> entities);
}
