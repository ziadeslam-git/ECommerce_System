using ECommerce_System.Data;
using ECommerce_System.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ECommerce_System.Utilities.DBInitializer;

public class DBInitializer : IDBInitializer
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ILogger<DBInitializer> _logger;
    private readonly IConfiguration _configuration;

    public DBInitializer(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ILogger<DBInitializer> logger,
        IConfiguration configuration)
    {
        _db          = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger      = logger;
        _configuration = configuration;
    }

    public async Task InitializeAsync()
    {
        // Apply any pending migrations automatically
        try
        {
            if ((await _db.Database.GetPendingMigrationsAsync()).Any())
            {
                await _db.Database.MigrateAsync();
                _logger.LogInformation("Database migration applied successfully.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while applying database migrations.");
            throw;
        }

        // ─── Seed Roles ───────────────────────────────────────
        string[] roles = [SD.Role_Admin, SD.Role_Customer];

        foreach (var role in roles)
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                await _roleManager.CreateAsync(new IdentityRole(role));
                _logger.LogInformation("Role '{Role}' created.", role);
            }
        }

        // ─── Seed Default Admin User ───────────────────────────
        var adminEmail = _configuration["AdminBootstrap:Email"];
        var adminPassword = _configuration["AdminBootstrap:Password"];
        var adminFullName = _configuration["AdminBootstrap:FullName"];

        if (string.IsNullOrWhiteSpace(adminEmail) ||
            string.IsNullOrWhiteSpace(adminPassword) ||
            string.IsNullOrWhiteSpace(adminFullName))
        {
            _logger.LogWarning(
                "Admin bootstrap skipped because one or more AdminBootstrap configuration values are missing.");
            return;
        }

        if (await _userManager.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new ApplicationUser
            {
                UserName        = adminEmail,
                Email           = adminEmail,
                NormalizedEmail = adminEmail.ToUpperInvariant(),
                FullName        = adminFullName,
                EmailConfirmed  = true,
                IsActive        = true,
                CreatedAt       = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(admin, adminPassword);

            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(admin, SD.Role_Admin);
                _logger.LogInformation("Default admin user '{Email}' created and assigned to Admin role.", adminEmail);
            }
            else
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                _logger.LogError("Failed to create admin user: {Errors}", errors);
            }
        }
    }
}
