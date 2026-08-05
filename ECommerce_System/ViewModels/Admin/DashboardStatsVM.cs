namespace ECommerce_System.ViewModels.Admin;

/// <summary>
/// Aggregated dashboard statistics returned by IDashboardService.GetStatsAsync().
/// Replaces the 5 separate ViewBag assignments in DashboardController.
/// </summary>
public sealed class DashboardStatsVM
{
    public int     TotalProducts  { get; init; }
    public int     TotalOrders    { get; init; }
    public int     PendingOrders  { get; init; }
    public decimal TotalRevenue   { get; init; }
    public int     TotalCustomers { get; init; }

    public IList<RecentOrderVM>   RecentOrders   { get; init; } = [];
    public IList<TopProductVM>    TopProducts    { get; init; } = [];
    public decimal[]              MonthlyRevenue { get; init; } = new decimal[12];
}

/// <summary>A lightweight order row for the dashboard "Recent Orders" section.</summary>
public sealed class RecentOrderVM
{
    public int      Id           { get; init; }
    public string   CustomerName { get; init; } = string.Empty;
    public decimal  TotalAmount  { get; init; }
    public string   Status       { get; init; } = string.Empty;
    public DateTime CreatedAt    { get; init; }
}
