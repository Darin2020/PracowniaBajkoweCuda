namespace BajkoweCuda.Application.Common;

/// <summary>What kind of failure an <see cref="AppError"/> is; the API maps it to an HTTP status.</summary>
public enum ErrorKind
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    TooManyRequests,
}

/// <summary>
/// A failure with a stable machine-readable code (for example <c>auth.invalid_credentials</c>). The API returns it as
/// the <c>code</c> of a ProblemDetails response; the frontend translates codes into messages (docs/PLAN.md §4.2).
/// </summary>
public sealed record AppError(string Code, ErrorKind Kind, IReadOnlyDictionary<string, string[]>? Details = null);
