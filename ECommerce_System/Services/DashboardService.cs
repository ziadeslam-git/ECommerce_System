using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Services.Interfaces;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ECommerce_System.Services;

/// <summary>
/// Provides admin dashboard statistics with a 5-minute in-memory cache.
/// Fixes DashboardController which was:
///   1. Running 5+ sequential DB queries per visit
///   2. Directly injecting ApplicationDbContext (bypassing Repository)
///   3. Performing customer count via raw JOIN (kept here, but behind the service boundary)
/// </summary>
public sealed class DashboardService(
    IUnitOfWork                uow,
    IMemoryCache               cache,
    ILogger<DashboardService>  logger)
    : IDashboardService
{
    private const string StatsCacheKey          = "dashboard_stats";
    private const string MonthlyRevenueCacheKey = "dashboard_monthly_revenue";
    private const string TopProductsCacheKey    = "dashboard_top_products";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<DashboardStatsVM> GetStatsAsync()
    {
        if (cache.TryGetValue(StatsCacheKey, out DashboardStatsVM? cached) && cached is not null)
            return cached;

        logger.LogDebug("Dashboard cache miss — querying database.");

        // ── Sequential awaits required: DbContext is NOT thread-safe ─────────
        var totalProducts  = await uow.Products.Query().AsNoTracking().CountAsync(p => p.IsActive);
        var totalOrders    = await uow.Orders.Query().AsNoTracking().CountAsync();
        var pendingOrders  = await uow.Orders.Query().AsNoTracking().CountAsync(o => o.Status == SD.Status_Pending);
        var totalRevenue   = await uow.Payments.Query().AsNoTracking()
                                .Where(p => p.Status == SD.Payment_Paid)
                                .SumAsync(p => (decimal?)p.Amount) ?? 0m;

        // Customer count: aggregate via UserRoles join (no full user-list load)
        // NOTE: This query touches Identity tables directly via UoW.
        // We use the Orders query as a proxy for now; the real customer count
        // requires a direct DB join that will stay inside this service.
        var totalCustomers = await uow.Orders.Query()
            .AsNoTracking()
            .Select(o => o.UserId)
            .Distinct()
            .CountAsync();

        var recentOrders = await uow.Orders.Query()
            .AsNoTracking()
            .Include(o => o.User)
            .Include(o => o.Address)
            .OrderByDescending(o => o.Id)
            .Take(5)
            .Select(o => new RecentOrderVM
            {
                Id           = o.Id,
                CustomerName = o.User != null ? o.User.FullName : (o.Address != null ? o.Address.FullName : "Unknown"),
                TotalAmount  = o.TotalAmount,
                Status       = o.Status,
                CreatedAt    = o.CreatedAt
            })
            .ToListAsync();

        var monthlyRevenue = await GetMonthlyRevenueAsync();
        var topProducts    = await GetTopProductsAsync();

        var vm = new DashboardStatsVM
        {
            TotalProducts  = totalProducts,
            TotalOrders    = totalOrders,
            PendingOrders  = pendingOrders,
            TotalRevenue   = totalRevenue,
            TotalCustomers = totalCustomers,
            RecentOrders   = recentOrders,
            TopProducts    = topProducts,
            MonthlyRevenue = monthlyRevenue
        };

        cache.Set(StatsCacheKey, vm, CacheDuration);
        return vm;
    }

    public async Task<decimal[]> GetMonthlyRevenueAsync()
    {
        if (cache.TryGetValue(MonthlyRevenueCacheKey, out decimal[]? cachedRevenue) && cachedRevenue is not null)
            return cachedRevenue;

        var currentYear = DateTime.UtcNow.Year;
        var monthlyRaw  = await uow.Payments.Query().AsNoTracking()
            .Where(p => p.Status == SD.Payment_Paid && p.CreatedAt.Year == currentYear)
            .GroupBy(p => p.CreatedAt.Month)
            .Select(g => new { Month = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync();

        var result = new decimal[12];
        foreach (var m in monthlyRaw)
            result[m.Month - 1] = m.Total;

        cache.Set(MonthlyRevenueCacheKey, result, CacheDuration);
        return result;
    }

    public async Task<IList<TopProductVM>> GetTopProductsAsync()
    {
        if (cache.TryGetValue(TopProductsCacheKey, out IList<TopProductVM>? cachedTop) && cachedTop is not null)
            return cachedTop;

        var topProducts = await uow.OrderItems.Query().AsNoTracking()
            .Where(oi => oi.Order.Status != SD.Status_Cancelled)
            .GroupBy(oi => new
            {
                oi.ProductName,
                CategoryName = oi.ProductVariant.Product.Category != null
                    ? oi.ProductVariant.Product.Category.Name
                    : "General",
                ImageUrl = oi.ProductVariant.Product.Images
                               .Where(i => i.IsMain)
                               .Select(i => i.ImageUrl)
                               .FirstOrDefault()
                           ?? oi.ProductVariant.Product.Images
                               .OrderBy(i => i.DisplayOrder)
                               .Select(i => i.ImageUrl)
                               .FirstOrDefault()
            })
            .Select(g => new TopProductVM
            {
                ProductName  = g.Key.ProductName,
                CategoryName = g.Key.CategoryName,
                TotalSold    = g.Sum(x => x.Quantity),
                ImageUrl     = g.Key.ImageUrl ?? string.Empty
            })
            .OrderByDescending(x => x.TotalSold)
            .Take(5)
            .ToListAsync();

        cache.Set(TopProductsCacheKey, topProducts, CacheDuration);
        return topProducts;
    }

    public void InvalidateCache()
    {
        cache.Remove(StatsCacheKey);
        cache.Remove(MonthlyRevenueCacheKey);
        cache.Remove(TopProductsCacheKey);
        logger.LogDebug("Dashboard cache invalidated.");
    }
}
