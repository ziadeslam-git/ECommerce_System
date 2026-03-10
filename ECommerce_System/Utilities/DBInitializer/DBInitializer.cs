using ECommerce_System.Data;
using ECommerce_System.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ECommerce_System.Utilities.DBInitializer;

public class DBInitializer : IDBInitializer
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ILogger<DBInitializer> _logger;

    public DBInitializer(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ILogger<DBInitializer> logger)
    {
        _db          = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger      = logger;
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
        if (await _userManager.FindByEmailAsync(SD.Admin_Email) is null)
        {
            var admin = new ApplicationUser
            {
                UserName        = SD.Admin_Email,
                Email           = SD.Admin_Email,
                NormalizedEmail = SD.Admin_Email.ToUpperInvariant(),
                FullName        = SD.Admin_FullName,
                EmailConfirmed  = true,
                IsActive        = true,
                CreatedAt       = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(admin, SD.Admin_Password);

            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(admin, SD.Role_Admin);
                _logger.LogInformation("Default admin user '{Email}' created and assigned to Admin role.", SD.Admin_Email);
            }
            else
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                _logger.LogError("Failed to create admin user: {Errors}", errors);
            }
        }
    }
}
