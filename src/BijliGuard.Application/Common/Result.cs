namespace BijliGuard.Application.Common;

public sealed record FieldError(string Field, string Message);

public sealed record Error(string Code, string Message, IReadOnlyList<FieldError>? Fields = null);

public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public Error? Error { get; }

    public static Result Success() => new(true, null);

    public static Result Failure(string code, string message, IReadOnlyList<FieldError>? fields = null) =>
        new(false, new Error(code, message, fields));
}

public sealed class Result<T> : Result
{
    private Result(T? value, bool isSuccess, Error? error) : base(isSuccess, error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Success(T value) => new(value, true, null);

    public new static Result<T> Failure(string code, string message, IReadOnlyList<FieldError>? fields = null) =>
        new(default, false, new Error(code, message, fields));
}
