namespace ECommerce.Shared;

/// <summary>
/// Represents the outcome of an operation without relying on exceptions for control flow.
/// Application services return Result/Result&lt;T&gt; instead of throwing for expected failure cases
/// (validation, not found, conflict) — exceptions are reserved for truly unexpected failures.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }
    public string? ErrorCode { get; }

    protected Result(bool isSuccess, string? error, string? errorCode)
    {
        if (isSuccess && error is not null)
        {
            throw new InvalidOperationException("A successful result cannot carry an error message.");
        }

        IsSuccess = isSuccess;
        Error = error;
        ErrorCode = errorCode;
    }

    public static Result Success() => new(true, null, null);
    public static Result Failure(string error, string errorCode = "BAD_REQUEST") => new(false, error, errorCode);

    public static Result<T> Success<T>(T value) => new(value, true, null, null);
    public static Result<T> Failure<T>(string error, string errorCode = "BAD_REQUEST") => new(default, false, error, errorCode);

    public static Result NotFound(string entityName, object key) =>
        Failure($"{entityName} with id '{key}' was not found.", "NOT_FOUND");

    public static Result<T> NotFound<T>(string entityName, object key) =>
        Failure<T>($"{entityName} with id '{key}' was not found.", "NOT_FOUND");
}

/// <summary>
/// Result carrying a success value. Accessing <see cref="Value"/> on a failed result throws,
/// mirroring the guard used by <see cref="Result"/> against invalid state.
/// </summary>
public class Result<T> : Result
{
    private readonly T? _value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failed result.");

    internal Result(T? value, bool isSuccess, string? error, string? errorCode)
        : base(isSuccess, error, errorCode)
    {
        _value = value;
    }

    public static implicit operator Result<T>(T value) => Success(value);
}
