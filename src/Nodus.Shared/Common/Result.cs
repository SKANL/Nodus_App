using System.Diagnostics.CodeAnalysis;

namespace Nodus.Shared.Common;

/// <summary>
/// Represents the result of an operation that can succeed or fail.
/// Provides explicit error handling without exceptions for expected failures.
/// </summary>
/// <typeparam name="T">The type of the success value.</typeparam>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly string? _error;
    private readonly Exception? _exception;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    [MemberNotNullWhen(true, nameof(_value))]
    [MemberNotNullWhen(true, nameof(Value))]
    public bool HasValue => IsSuccess && _value is not null;

    public T? Value => IsSuccess ? _value : throw new InvalidOperationException($"Cannot access Value of a failed Result. Error: {_error}");

    public string Error => IsFailure ? _error ?? "Unknown error" : throw new InvalidOperationException("Cannot access Error of a successful Result.");

    public Exception? Exception => _exception;

    private Result(bool isSuccess, T? value, string? error, Exception? exception)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
        _exception = exception;
    }

    public static Result<T> Success(T value) => new(true, value, null, null);

    public static Result<T> Failure(string error, Exception? exception = null) => new(false, default, error, exception);

    /// <summary>
    /// Executes an action if the result is successful.
    /// </summary>
    public Result<T> OnSuccess(Action<T> action)
    {
        if (IsSuccess && _value is not null)
            action(_value);
        return this;
    }

    /// <summary>
    /// Executes an action if the result is a failure.
    /// </summary>
    public Result<T> OnFailure(Action<string> action)
    {
        if (IsFailure)
            action(Error);
        return this;
    }

    /// <summary>
    /// Maps the success value to a new type.
    /// </summary>
    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        return IsSuccess && _value is not null
            ? Result<TNew>.Success(mapper(_value))
            : Result<TNew>.Failure(Error, Exception);
    }

    /// <summary>
    /// Binds the result to a new result-returning function (flatMap/bind).
    /// </summary>
    public Result<TNew> Bind<TNew>(Func<T, Result<TNew>> binder)
    {
        return IsSuccess && _value is not null
            ? binder(_value)
            : Result<TNew>.Failure(Error, Exception);
    }

    /// <summary>
    /// Returns the value if successful, otherwise returns the default value.
    /// </summary>
    public T? ValueOr(T? defaultValue) => IsSuccess ? _value : defaultValue;

    /// <summary>
    /// Matches the result to one of two functions based on success/failure.
    /// </summary>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<string, TResult> onFailure)
    {
        return IsSuccess && _value is not null
            ? onSuccess(_value)
            : onFailure(Error);
    }

    public override string ToString() => IsSuccess ? $"Success({_value})" : $"Failure({Error})";
}

/// <summary>
/// Non-generic Result for operations that don't return a value.
/// </summary>
public readonly struct Result
{
    private readonly string? _error;
    private readonly Exception? _exception;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public string Error => IsFailure ? _error ?? "Unknown error" : throw new InvalidOperationException("Cannot access Error of a successful Result.");

    public Exception? Exception => _exception;

    private Result(bool isSuccess, string? error, Exception? exception)
    {
        IsSuccess = isSuccess;
        _error = error;
        _exception = exception;
    }

    public static Result Success() => new(true, null, null);

    public static Result Failure(string error, Exception? exception = null) => new(false, error, exception);

    public Result OnSuccess(Action action)
    {
        if (IsSuccess)
            action();
        return this;
    }

    public Result OnFailure(Action<string> action)
    {
        if (IsFailure)
            action(Error);
        return this;
    }

    public TResult Match<TResult>(Func<TResult> onSuccess, Func<string, TResult> onFailure)
    {
        return IsSuccess ? onSuccess() : onFailure(Error);
    }

    public override string ToString() => IsSuccess ? "Success" : $"Failure({Error})";
}
