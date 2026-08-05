namespace ECommerce_System.Common.Exceptions;

/// <summary>
/// Thrown when a business rule is violated (e.g., cancelling a delivered order,
/// applying an expired coupon, etc.).
/// The global exception middleware maps this to HTTP 400 with a user-friendly message.
/// </summary>
public sealed class BusinessException(string message) : Exception(message);
