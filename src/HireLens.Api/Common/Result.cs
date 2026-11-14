using Microsoft.AspNetCore.Http.HttpResults;

namespace HireLens.Api.Common;

public enum ErrorKind { NotFound, Conflict, Invalid }

public sealed record Error(ErrorKind Kind, string Message)
{
    public static Error NotFound(string message) => new(ErrorKind.NotFound, message);
    public static Error Conflict(string message) => new(ErrorKind.Conflict, message);
    public static Error Invalid(string message) => new(ErrorKind.Invalid, message);

    public ProblemHttpResult ToProblem() => TypedResults.Problem(
        detail: Message,
        statusCode: Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        });
}

/// <summary>
/// The outcome of a service call: either a value or an expected error (not found, conflict, invalid).
/// Expected failures are values, not exceptions; exceptions are left for things that are actually broken.
/// </summary>
public sealed class Result<T>
{
    private Result(T? value, Error? error) => (Value, Error) = (value, error);

    public T? Value { get; }
    public Error? Error { get; }

    public static implicit operator Result<T>(T value) => new(value, null);
    public static implicit operator Result<T>(Error error) => new(default, error);
}
