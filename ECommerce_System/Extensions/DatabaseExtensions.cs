using ECommerce_System.Data;
using Microsoft.EntityFrameworkCore;

namespace ECommerce_System.Extensions;

public static class DatabaseExtensions
{
    /// <summary>
    /// Registers EF Core DbContextPool with SQL Server + retry-on-failure.
    /// Uses DbContextPool instead of AddDbContext for better throughput under concurrent requests.
    /// </summary>
    public static IServiceCollection AddDatabaseServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContextPool<ApplicationDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sqlOptions => sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null)));

        return services;
    }
}
