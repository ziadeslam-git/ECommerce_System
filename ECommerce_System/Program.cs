using ECommerce_System.Data;
using ECommerce_System.Models;
using ECommerce_System.Repositories;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.Utilities.DBInitializer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Stripe;

var builder = WebApplication.CreateBuilder(args);

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
    options.Password.RequireDigit           = true;
    options.Password.RequiredLength         = 8;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase       = true;
    options.Password.RequireLowercase       = true;

    options.User.RequireUniqueEmail         = true;
    options.SignIn.RequireConfirmedEmail     = false;  // set true to enforce email confirmation
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Cookie authentication settings
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath          = "/Identity/Account/Login";
    options.LogoutPath         = "/Identity/Account/Logout";
    options.AccessDeniedPath   = "/Identity/Account/AccessDenied";
    options.ExpireTimeSpan     = TimeSpan.FromDays(14);
    options.SlidingExpiration  = true;
    options.Cookie.HttpOnly    = true;
    options.Cookie.IsEssential = true;
});

// ──────────────────────────────────────────────────────────────────
// 3. Stripe Settings
// ──────────────────────────────────────────────────────────────────
builder.Services.Configure<StripeSettings>(
    builder.Configuration.GetSection("Stripe"));

StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

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
// 7. External Authentication (Google & Facebook)
// ──────────────────────────────────────────────────────────────────
builder.Services.AddAuthentication()
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
    })
    .AddFacebook(options =>
    {
        options.AppId = builder.Configuration["Authentication:Facebook:AppId"]!;
        options.AppSecret = builder.Configuration["Authentication:Facebook:AppSecret"]!;
        
        // Facebook automatically requests "email" but if the app hasn't been verified 
        // to request this scope, it throws "Invalid scopes: email".
        // We override this behavior by explicitly clearing the default scopes and 
        // asking only for "public_profile".
        options.Scope.Clear();
        options.Scope.Add("public_profile");
    });

// ──────────────────────────────────────────────────────────────────
// 6. MVC with Views
// ──────────────────────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// ──────────────────────────────────────────────────────────────────
var app = builder.Build();
// ──────────────────────────────────────────────────────────────────

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// ─── Seed Database on startup ───────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<IDBInitializer>();
    await initializer.InitializeAsync();
}

// ─── Area + Default Routing ─────────────────────────────────────
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
