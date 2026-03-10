using ECommerce_System.Data;
using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;

namespace ECommerce_System.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    // ─── Specific Repositories ───
    public IProductRepository Products { get; private set; }
    public IOrderRepository Orders { get; private set; }
    public ICartRepository Carts { get; private set; }
    public ICategoryRepository Categories { get; private set; }

    // ─── Generic Repositories ───
    public IRepository<ProductVariant> ProductVariants { get; private set; }
    public IRepository<ProductImage> ProductImages { get; private set; }
    public IRepository<CartItem> CartItems { get; private set; }
    public IRepository<Address> Addresses { get; private set; }
    public IRepository<OrderItem> OrderItems { get; private set; }
    public IRepository<Payment> Payments { get; private set; }
    public IRepository<Shipment> Shipments { get; private set; }
    public IRepository<WishlistItem> WishlistItems { get; private set; }
    public IRepository<Discount> Discounts { get; private set; }
    public IRepository<Review> Reviews { get; private set; }

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;

        Products       = new ProductRepository(_context);
        Orders         = new OrderRepository(_context);
        Carts          = new CartRepository(_context);
        Categories     = new CategoryRepository(_context);
        ProductVariants = new Repository<ProductVariant>(_context);
        ProductImages   = new Repository<ProductImage>(_context);
        CartItems       = new Repository<CartItem>(_context);
        Addresses       = new Repository<Address>(_context);
        OrderItems      = new Repository<OrderItem>(_context);
        Payments        = new Repository<Payment>(_context);
        Shipments       = new Repository<Shipment>(_context);
        WishlistItems   = new Repository<WishlistItem>(_context);
        Discounts       = new Repository<Discount>(_context);
        Reviews         = new Repository<Review>(_context);
    }

    public async Task<int> SaveAsync()
        => await _context.SaveChangesAsync();

    public void Dispose()
        => _context.Dispose();
}
