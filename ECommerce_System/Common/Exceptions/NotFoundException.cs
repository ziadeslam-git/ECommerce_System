namespace ECommerce_System.Common.Exceptions;

/// <summary>
/// Thrown when a requested resource does not exist.
/// The global exception middleware maps this to HTTP 404.
/// </summary>
public sealed class NotFoundException(string message) : Exception(message)
{
    public static NotFoundException For<T>(int id)
        => new($"{typeof(T).Name} with id '{id}' was not found.");

    public static NotFoundException For<T>(string identifier)
        => new($"{typeof(T).Name} '{identifier}' was not found.");
}
