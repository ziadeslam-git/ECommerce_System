using ECommerce_System.Common;
using ECommerce_System.Models;

namespace ECommerce_System.Services.Interfaces;

/// <summary>
/// Handles all shopping cart business logic extracted from Customer/CartController.
/// Owns: add/remove/update cart items, stock validation, cart count caching.
/// </summary>
public interface ICartService
{
    /// <summary>Loads the full cart for display, including bundle variant stock lookups.</summary>
    Task<Cart?> GetCartAsync(string userId);

    /// <summary>Returns the number of distinct line items in the user's cart (for nav badge).</summary>
    Task<int> GetCartCountAsync(string userId);

    /// <summary>Adds a product variant to the cart, or increments quantity if already present.</summary>
    Task<Result<CartItem>> AddToCartAsync(string userId, int productVariantId, int quantity);

    /// <summary>Removes a specific cart item (validates ownership).</summary>
    Task<Result<bool>> RemoveFromCartAsync(string userId, int cartItemId);

    /// <summary>Sets the quantity of a cart item (0 = remove). Validates stock.</summary>
    Task<Result<CartItem>> UpdateQuantityAsync(string userId, int cartItemId, int newQuantity);

    /// <summary>Adds a gift bundle (all its variants) to the cart as a single line item.</summary>
    Task<Result<CartItem>> AddBundleToCartAsync(string userId, int giftBundleId, int quantity);

    /// <summary>Clears all items from the user's cart (called after order placement).</summary>
    Task ClearCartAsync(string userId);
}
