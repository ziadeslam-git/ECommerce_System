namespace ECommerce_System.Common;

/// <summary>
/// Represents the outcome of an operation — either success with a value, or failure with an error message.
/// Use this instead of throwing exceptions for expected/business errors.
/// </summary>
public sealed class Result<T>
{
    public bool     IsSuccess { get; }
    public bool     IsFailure => !IsSuccess;
    public T?       Value     { get; }
    public string?  Error     { get; }

    private Result(bool success, T? value, string? error)
        => (IsSuccess, Value, Error) = (success, value, error);

    public static Result<T> Success(T value)   => new(true,  value,   null);
    public static Result<T> Failure(string err) => new(false, default, err);

    /// <summary>Unwrap as nullable — returns Value on success, default on failure.</summary>
    public T? ValueOrDefault() => Value;

    public override string ToString()
        => IsSuccess ? $"Success({Value})" : $"Failure({Error})";
}

/// <summary>
/// Non-generic Result for operations that do not return a value (void).
/// </summary>
public sealed class Result
{
    public bool    IsSuccess { get; }
    public bool    IsFailure => !IsSuccess;
    public string? Error     { get; }

    private Result(bool success, string? error)
        => (IsSuccess, Error) = (success, error);

    public static Result Success()             => new(true,  null);
    public static Result Failure(string error) => new(false, error);

    public override string ToString()
        => IsSuccess ? "Success" : $"Failure({Error})";
}
