using ECommerce_System.Services;
using ECommerce_System.Services.Interfaces;

namespace ECommerce_System.Extensions;

/// <summary>
/// Registers all custom application services (Service Layer).
/// Add new services here — keeps Program.cs clean.
/// </summary>
public static class ApplicationServicesExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Service Layer ──────────────────────────────────────────────────────
        services.AddScoped<IDiscountService, DiscountService>();
        services.AddScoped<IOrderService,    OrderService>();
        services.AddScoped<IDashboardService, DashboardService>();

        // ICheckoutService and ICartService will be registered once implemented
        // services.AddScoped<ICheckoutService, CheckoutService>();
        // services.AddScoped<ICartService, CartService>();

        // ── Caching ────────────────────────────────────────────────────────────
        services.AddMemoryCache();

        // ── Response Compression (Brotli preferred, Gzip fallback) ─────────────
        services.AddResponseCompression(opts =>
        {
            opts.EnableForHttps = true;
            opts.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
            opts.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
            opts.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(
            [
                "image/svg+xml",
                "application/json",
                "text/css",
                "application/javascript"
            ]);
        });
        services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(options =>
            options.Level = System.IO.Compression.CompressionLevel.Fastest);
        services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(options =>
            options.Level = System.IO.Compression.CompressionLevel.Fastest);

        return services;
    }
}
