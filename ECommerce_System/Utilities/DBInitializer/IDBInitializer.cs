namespace ECommerce_System.Utilities.DBInitializer;

public interface IDBInitializer
{
    /// <summary>
    /// Creates roles, seeds admin user, and applies pending migrations on startup.
    /// Call this once during application initialization.
    /// </summary>
    Task InitializeAsync();
}
