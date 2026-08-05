using ECommerce_System.Models;
using ECommerce_System.Utilities.Localization;
using Microsoft.AspNetCore.Identity;

namespace ECommerce_System.Extensions;

public static class IdentityExtensions
{
    /// <summary>
    /// Configures ASP.NET Core Identity with password policy, cookie settings,
    /// security stamp validation interval, and localized error messages.
    /// </summary>
    public static IServiceCollection AddIdentityServices(this IServiceCollection services)
    {
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit           = true;
            options.Password.RequiredLength         = 8;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequireUppercase       = true;
            options.Password.RequireLowercase       = true;

            options.User.RequireUniqueEmail = true;
            // NOTE: Email confirmation is optional in dev phase.
            // Change to true in production after SMTP is verified and tested.
            options.SignIn.RequireConfirmedEmail = true;
        })
        .AddEntityFrameworkStores<Data.ApplicationDbContext>()
        .AddDefaultTokenProviders()
        .AddErrorDescriber<LocalizedIdentityErrorDescriber>();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath       = "/Identity/Account/Login";
            options.LogoutPath      = "/Identity/Account/Logout";
            options.AccessDeniedPath = "/Identity/Account/AccessDenied";
            options.ExpireTimeSpan  = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
            options.Cookie.HttpOnly  = true;
            options.Cookie.IsEssential = true;
            options.Cookie.MaxAge    = TimeSpan.FromDays(30);
            options.Cookie.SameSite  = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        // SecurityStamp: forces re-authentication within 30 min of privilege changes
        // (e.g. admin deactivates user → session invalidated without per-request DB hit)
        services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.FromMinutes(30);
        });

        return services;
    }
}
