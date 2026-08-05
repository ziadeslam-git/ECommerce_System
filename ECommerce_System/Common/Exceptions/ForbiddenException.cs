namespace ECommerce_System.Common.Exceptions;

/// <summary>
/// Thrown when a user tries to access or modify a resource that does not belong to them (IDOR protection).
/// The global exception middleware maps this to HTTP 403.
/// </summary>
public sealed class ForbiddenException(string message = "You do not have permission to access this resource.")
    : Exception(message);
