using ECommerce_System.Data;
using ECommerce_System.Models;
using ECommerce_System.Repositories;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Resources;
using ECommerce_System.Utilities;
using ECommerce_System.Utilities.DBInitializer;
using ECommerce_System.Utilities.Localization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Stripe;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
Directory.CreateDirectory(dataProtectionKeysPath);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("SmartStore");

// ──────────────────────────────────────────────────────────────────
// 1. Database — EF Core with SQL Server
// ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ──────────────────────────────────────────────────────────────────
// 2. ASP.NET Core Identity — Cookie Authentication
// ──────────────────────────────────────────────────────────────────
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;

    options.User.RequireUniqueEmail = true;
    // NOTE: Email confirmation is optional in dev phase.
    // Change to true in production after SMTP is verified and tested.
    options.SignIn.RequireConfirmedEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders()
.AddErrorDescriber<LocalizedIdentityErrorDescriber>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.MaxAge = TimeSpan.FromDays(30);
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

// SecurityStamp validator: ASP.NET Identity re-validates the user's security stamp
// at this interval. When an admin calls UpdateSecurityStampAsync(user) on deactivation,
// the user will be signed out automatically within this window — NO per-request DB hit.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(30);
});

// ──────────────────────────────────────────────────────────────────
// 3. Stripe Settings
// ──────────────────────────────────────────────────────────────────
builder.Services.Configure<StripeSettings>(
    builder.Configuration.GetSection("Stripe"));

StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

var configurationRoot = (IConfigurationRoot)builder.Configuration;
var stripePublishableKey = builder.Configuration["Stripe:PublishableKey"];
var stripeSecretKey = builder.Configuration["Stripe:SecretKey"];
var stripeWebhookSecret = builder.Configuration["Stripe:WebhookSecret"];
var publicBaseUrl = builder.Configuration["App:PublicBaseUrl"];

string[] GetProvidersWithNonEmptyValue(string key) =>
    configurationRoot.Providers
        .Where(provider => provider.TryGet(key, out var value) && !string.IsNullOrWhiteSpace(value))
        .Select(provider => provider.ToString() ?? provider.GetType().Name)
        .ToArray();

var stripePublishableProviders = GetProvidersWithNonEmptyValue("Stripe:PublishableKey");
var stripeSecretProviders = GetProvidersWithNonEmptyValue("Stripe:SecretKey");
var stripeWebhookProviders = GetProvidersWithNonEmptyValue("Stripe:WebhookSecret");

// ──────────────────────────────────────────────────────────────────
// 4. Cloudinary — Image Upload Service
// ──────────────────────────────────────────────────────────────────
builder.Services.Configure<CloudinarySettings>(
    builder.Configuration.GetSection("Cloudinary"));
builder.Services.AddScoped<ICloudinaryService, CloudinaryService>();

// ──────────────────────────────────────────────────────────────────
// 4. Repository Pattern — Unit of Work
// ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// ──────────────────────────────────────────────────────────────────
// 5. DB Initializer (roles + admin seed)
// ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IDBInitializer, DBInitializer>();

// ──────────────────────────────────────────────────────────────────
// 6. Email Sender — Gmail SMTP
// ──────────────────────────────────────────────────────────────────
builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddTransient<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, EmailSender>();

// ──────────────────────────────────────────────────────────────────
// 7. External Authentication — Google & Facebook (with Guards)
//    ⚠️ If User Secrets are not set on this machine,
//    the provider is SKIPPED gracefully instead of crashing.
// ──────────────────────────────────────────────────────────────────
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
var facebookAppId = builder.Configuration["Authentication:Facebook:AppId"];
var facebookAppSecret = builder.Configuration["Authentication:Facebook:AppSecret"];

var authBuilder = builder.Services.AddAuthentication();

if (!string.IsNullOrWhiteSpace(googleClientId) &&
    !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
    });
    Console.WriteLine("[STARTUP] Google OAuth: configured ✅");
}
else
{
    Console.WriteLine("[STARTUP] Google OAuth: User Secrets not found — skipping ⚠️");
}

if (!string.IsNullOrWhiteSpace(facebookAppId) &&
    !string.IsNullOrWhiteSpace(facebookAppSecret))
{
    authBuilder.AddFacebook(options =>
    {
        options.AppId = facebookAppId;
        options.AppSecret = facebookAppSecret;
        options.Scope.Clear();
        options.Scope.Add("public_profile");
    });
    Console.WriteLine("[STARTUP] Facebook OAuth: configured ✅");
}
else
{
    Console.WriteLine("[STARTUP] Facebook OAuth: User Secrets not found — skipping ⚠️");
}

// ──────────────────────────────────────────────────────────────────
// 8. Localization — i18n (ar-EG / en-US)
// ──────────────────────────────────────────────────────────────────
// AddLocalization MUST be registered before AddControllersWithViews
// so that AddViewLocalization() can resolve IStringLocalizerFactory.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { "ar-EG", "en-US" };

    options
        .SetDefaultCulture("en-US")
        .AddSupportedCultures(supportedCultures)
        .AddSupportedUICultures(supportedCultures);

    // Clear ALL default providers (QueryString, Cookie, AcceptLanguage).
    // We only want the Cookie provider — no Accept-Language header leaking
    // in, no ?culture= query-string overriding the user's saved preference.
    options.RequestCultureProviders.Clear();
    options.RequestCultureProviders.Add(new CookieRequestCultureProvider
    {
        // Use the standard ASP.NET Core cookie name (.AspNetCore.Culture)
        // so the SetLanguage action and the middleware share the same key.
        CookieName = CookieRequestCultureProvider.DefaultCookieName
    });
});

// ──────────────────────────────────────────────────────────────────
// 9. MVC with Views
// ──────────────────────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", cfg =>
    {
        cfg.PermitLimit         = 10;
        cfg.Window              = TimeSpan.FromMinutes(1);
        cfg.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        cfg.QueueLimit          = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
builder.Services.AddControllersWithViews()
    .AddViewLocalization()  // picks up IStringLocalizerFactory registered above
    .AddDataAnnotationsLocalization(options =>
    {
        options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource));
    }); // localizes [Required], [Display] etc.
builder.Services.AddRazorPages();

// ──────────────────────────────────────────────────────────────────
var app = builder.Build();
// ──────────────────────────────────────────────────────────────────

app.Logger.LogInformation(
    "Startup config diagnostics | Environment={Environment} | PublicBaseUrl={PublicBaseUrl} | DataProtectionKeysPath={DataProtectionKeysPath} | StripePublishableConfigured={StripePublishableConfigured} | StripePublishableLength={StripePublishableLength} | StripePublishableProviders={StripePublishableProviders} | StripeSecretConfigured={StripeSecretConfigured} | StripeSecretLength={StripeSecretLength} | StripeSecretProviders={StripeSecretProviders} | StripeWebhookConfigured={StripeWebhookConfigured} | StripeWebhookLength={StripeWebhookLength} | StripeWebhookProviders={StripeWebhookProviders}",
    app.Environment.EnvironmentName,
    string.IsNullOrWhiteSpace(publicBaseUrl) ? "(null)" : publicBaseUrl,
    dataProtectionKeysPath,
    !string.IsNullOrWhiteSpace(stripePublishableKey),
    stripePublishableKey?.Length ?? 0,
    stripePublishableProviders.Length == 0 ? "none" : string.Join(" | ", stripePublishableProviders),
    !string.IsNullOrWhiteSpace(stripeSecretKey),
    stripeSecretKey?.Length ?? 0,
    stripeSecretProviders.Length == 0 ? "none" : string.Join(" | ", stripeSecretProviders),
    !string.IsNullOrWhiteSpace(stripeWebhookSecret),
    stripeWebhookSecret?.Length ?? 0,
    stripeWebhookProviders.Length == 0 ? "none" : string.Join(" | ", stripeWebhookProviders));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRequestLocalization();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
// ── Deactivated-user check ─────────────────────────────────────────────────
// IMPORTANT: The previous implementation called userManager.GetUserAsync() on
// EVERY request (including static files and AJAX calls), causing a DB round-trip
// per request and making the entire app feel slow.
//
// Fix: Use SecurityStamp validation instead.
// When an admin deactivates a user (UsersController.ToggleActive), call
//   await userManager.UpdateSecurityStampAsync(user)
// ASP.NET Identity's cookie middleware will then invalidate the session
// automatically on the NEXT request — without a DB hit on every request.
//
// The interval below (30 min) matches the Identity default. You can lower it
// (e.g. TimeSpan.FromMinutes(5)) if faster propagation is needed.

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<IDBInitializer>();
    await initializer.InitializeAsync();
}

app.MapAreaControllerRoute(
    name: "AdminArea",
    areaName: "Admin",
    pattern: "Admin/{controller=Dashboard}/{action=Index}/{id?}");


app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{area=Customer}/{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();
