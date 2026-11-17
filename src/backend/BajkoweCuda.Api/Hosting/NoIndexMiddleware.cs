namespace BajkoweCuda.Api.Hosting;

/// <summary>
/// Keeps a non-production environment out of search engines (docs/PLAN.md §7): every response carries
/// <c>X-Robots-Tag: noindex, nofollow</c> and <c>/robots.txt</c> disallows everything.
/// Registered only when <c>Seo:NoIndex</c> is true.
/// </summary>
internal sealed class NoIndexMiddleware(RequestDelegate next)
{
    public const string RobotsTxt = "User-agent: *\nDisallow: /\n";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";

        if (context.Request.Path.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase)
            && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers.CacheControl = StaticAssetCaching.Other;

            return HttpMethods.IsHead(context.Request.Method)
                ? Task.CompletedTask
                : context.Response.WriteAsync(RobotsTxt, context.RequestAborted);
        }

        return next(context);
    }
}
