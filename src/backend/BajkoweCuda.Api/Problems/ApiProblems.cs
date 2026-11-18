using BajkoweCuda.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace BajkoweCuda.Api.Problems;

/// <summary>
/// RFC 7807 responses with a stable <c>code</c> extension (docs/PLAN.md §4.2). The frontend translates the code;
/// the title is only a hint for developers and is never shown to users.
/// </summary>
internal static class ApiProblems
{
    public const string CodeKey = "code";
    public const string ValidationFailed = "validation_failed";
    public const string AntiforgeryInvalid = "antiforgery.invalid";

    public static int StatusOf(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static ProblemDetails Create(AppError error)
    {
        var problem = error.Details is { Count: > 0 } details
            ? new ValidationProblemDetails(details.ToDictionary(pair => pair.Key, pair => pair.Value))
            : new ProblemDetails();

        problem.Status = StatusOf(error.Kind);
        problem.Title = error.Code;
        problem.Extensions[CodeKey] = error.Code;
        return problem;
    }

    public static ObjectResult ToResult(this AppError error) =>
        new(Create(error)) { StatusCode = StatusOf(error.Kind), ContentTypes = { "application/problem+json" } };

    /// <summary>For middleware and authentication events, which have no MVC action context.</summary>
    public static Task WriteAsync(HttpContext context, AppError error)
    {
        var problem = Create(error);
        context.Response.StatusCode = problem.Status!.Value;
        return context.RequestServices.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem })
            .AsTask();
    }

    /// <summary>Model validation failures: <c>400 validation_failed</c> with the field errors.</summary>
    public static IActionResult InvalidModelState(ActionContext context)
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = ValidationFailed,
        };
        problem.Extensions[CodeKey] = ValidationFailed;
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }
}
