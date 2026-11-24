using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BajkoweCuda.Api.Problems;

/// <summary>
/// The server's refusals of a malformed or oversized request (<see cref="BadHttpRequestException"/>, thrown when the
/// body is read) are the client's fault: they keep their status (413 for a body over the limit, 400 otherwise) instead
/// of turning into a 500, with a stable <c>code</c> like every other error.
/// </summary>
internal sealed class BadRequestExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public const string TooLarge = "request_too_large";
    public const string BadRequest = "bad_request";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException bad)
        {
            return false;
        }

        var code = bad.StatusCode == StatusCodes.Status413PayloadTooLarge ? TooLarge : BadRequest;
        var problem = new ProblemDetails { Status = bad.StatusCode, Title = code };
        problem.Extensions[ApiProblems.CodeKey] = code;

        httpContext.Response.StatusCode = bad.StatusCode;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception });
    }
}
