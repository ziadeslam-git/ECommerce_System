using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;

namespace ECommerce_System.Repositories.IRepositories;

public interface IUnitOfWork : IDisposable
{
    IProductRepository Products { get; }
    IOrderRepository Orders { get; }
    ICartRepository Carts { get; }
    ICategoryRepository Categories { get; }
    IRepository<ProductVariant> ProductVariants { get; }
    IRepository<ProductImage> ProductImages { get; }
    IRepository<CartItem> CartItems { get; }
    IRepository<Address> Addresses { get; }
    IRepository<OrderItem> OrderItems { get; }
    IRepository<Payment> Payments { get; }
    IRepository<Shipment> Shipments { get; }
    IRepository<WishlistItem> WishlistItems { get; }
    IRepository<Discount> Discounts { get; }
    IRepository<Review> Reviews { get; }

    /// <summary>Persists all pending changes to the database.</summary>
    Task<int> SaveAsync();

    /// <summary>Sets the RowVersion original value on a tracked entity for optimistic concurrency.</summary>
    void SetRowVersion<T>(T entity, byte[] rowVersion) where T : class;
}
