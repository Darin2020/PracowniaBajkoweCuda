using BajkoweCuda.Api.Problems;
using BajkoweCuda.Application.Common;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BajkoweCuda.Api.Auth;

/// <summary>
/// Every unsafe request to a controller (POST, PUT, PATCH, DELETE) must carry the antiforgery header that matches the
/// cookies (docs/PLAN.md §8.1); otherwise it ends as <c>400 antiforgery.invalid</c>, so the frontend can fetch a fresh
/// token from <c>GET /api/auth/me</c> and retry. MVC's own AutoValidateAntiforgeryToken needs the view services,
/// which this API does not have.
/// </summary>
internal sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public static readonly AppError Invalid = new(ApiProblems.AntiforgeryInvalid, ErrorKind.Validation);

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
        {
            return;
        }

        if (!await antiforgery.IsRequestValidAsync(context.HttpContext))
        {
            context.Result = Invalid.ToResult();
        }
    }
}
