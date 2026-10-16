namespace CaseIntake.Contracts.Common;

public enum ErrorKind { Validation, NotFound, Forbidden, Conflict, InvalidTransition, ExternalUnavailable }

public sealed record Error(ErrorKind Kind, string Code, string Message,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);

/// <summary>Outcome of a service operation. A failure never carries a payload; a success payload is never null.</summary>
public sealed class Result<T>
{
    public T? Value { get; }
    public Error? Error { get; }
    public bool IsSuccess => Error is null;

    private Result(T? value, Error? error)
    {
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(
        value is null ? throw new ArgumentNullException(nameof(value)) : value, null);

    public static Result<T> Failure(Error error) => new(default,
        error ?? throw new ArgumentNullException(nameof(error)));
}
