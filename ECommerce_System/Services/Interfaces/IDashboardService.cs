using ECommerce_System.ViewModels.Admin;

namespace ECommerce_System.Services.Interfaces;

/// <summary>
/// Aggregates dashboard statistics with caching.
/// Extracted from DashboardController which was directly injecting ApplicationDbContext.
/// Uses IMemoryCache to avoid 5+ sequential DB round-trips on every admin dashboard visit.
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Returns all dashboard stats in a single cached call.
    /// Cache duration: 5 minutes (configurable).
    /// </summary>
    Task<DashboardStatsVM> GetStatsAsync();

    /// <summary>Returns the monthly revenue breakdown for the current year (cached).</summary>
    Task<decimal[]> GetMonthlyRevenueAsync();

    /// <summary>Returns the top 5 best-selling products (cached).</summary>
    Task<IList<TopProductVM>> GetTopProductsAsync();

    /// <summary>Invalidates all dashboard cache entries (call after order status changes).</summary>
    void InvalidateCache();
}
