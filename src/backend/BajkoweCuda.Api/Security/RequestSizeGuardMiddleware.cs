using BajkoweCuda.Api.Problems;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace BajkoweCuda.Api.Security;

/// <summary>
/// Refuses a request whose declared body is larger than its endpoint accepts before anything reads it (docs/PLAN.md
/// §8.2): <see cref="DefaultLimit"/>, or the endpoint's own <c>[RequestSizeLimit]</c> (the image uploads). Kestrel's
/// limit of the same size stays as the backstop for bodies without a <c>Content-Length</c>.
/// </summary>
internal sealed class RequestSizeGuardMiddleware(RequestDelegate next, IProblemDetailsService problems)
{
    /// <summary>JSON requests are small: the longest text is a 4,000-character order description.</summary>
    public const long DefaultLimit = 1024 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        var limit = context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize ?? DefaultLimit;

        if (context.Request.ContentLength is { } length && length > limit)
        {
            var problem = new ProblemDetails { Status = StatusCodes.Status413PayloadTooLarge, Title = BadRequestExceptionHandler.TooLarge };
            problem.Extensions[ApiProblems.CodeKey] = BadRequestExceptionHandler.TooLarge;
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            // The client may still be sending: do not keep the connection to drain megabytes nobody will read.
            context.Response.Headers.Connection = "close";
            await problems.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
            return;
        }

        await next(context);
    }
}
