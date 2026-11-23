namespace SystemX.Auth2.Contracts.Common;

/// <summary>Result of an operation without a value.</summary>
public record Result
{
    /// <summary>True when the operation worked.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>The error, or null on success.</summary>
    public ApiError? Error { get; init; }

    /// <summary>Creates a success result.</summary>
    public static Result Success() => new();

    /// <summary>Creates a failure result.</summary>
    public static Result Failure(string code, string? message = null) => new() { Error = new ApiError(code, message) };
}

/// <summary>Result of an operation with a value.</summary>
/// <typeparam name="T">Value type.</typeparam>
public sealed record Result<T> : Result
{
    /// <summary>The value. Only set on success.</summary>
    public T? Value { get; init; }

    /// <summary>Creates a success result.</summary>
    public static Result<T> Success(T value) => new() { Value = value };

    /// <summary>Creates a failure result.</summary>
    public static new Result<T> Failure(string code, string? message = null) => new() { Error = new ApiError(code, message) };
}
