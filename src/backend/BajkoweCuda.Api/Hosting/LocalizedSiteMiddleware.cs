using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace BajkoweCuda.Api.Hosting;

/// <summary>
/// Routes browser requests to the prerendered Angular site in <c>wwwroot</c> (docs/PLAN.md §4.4).
/// It only redirects or rewrites the request path; the static file middleware after it sends the file.
/// <list type="bullet">
/// <item><c>/</c> redirects to <c>/{lang}/</c> (cookie, then <c>Accept-Language</c>, then English).</item>
/// <item><c>/{lang}/page</c> is served from the prerendered <c>/{lang}/page/index.html</c> when it exists.</item>
/// <item>Any other page path under <c>/{lang}/</c> falls back to that locale's client-side shell.</item>
/// <item><c>/api</c>, <c>/hubs</c> and <c>/health</c> are never touched, so they can never fall back to HTML.</item>
/// </list>
/// </summary>
internal sealed class LocalizedSiteMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    /// <summary>Client-side-rendered shell that Angular emits per locale with <c>outputMode: "static"</c>.</summary>
    public const string ClientShellFileName = "index.csr.html";

    private const string PrerenderedPageFileName = "index.html";

    private static readonly PathString[] BackendPrefixes = ["/api", "/hubs", "/health"];

    private readonly IFileProvider _files = environment.WebRootFileProvider;

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return next(context);
        }

        var path = request.Path;

        if (Array.Exists(BackendPrefixes, prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return next(context);
        }

        if (!path.HasValue || path.Value == "/")
        {
            RedirectToNegotiatedLanguage(context);
            return Task.CompletedTask;
        }

        var value = path.Value.AsSpan();
        var afterLocale = value[1..].IndexOf('/');
        var locale = SupportedLocales.Find(afterLocale < 0 ? value[1..] : value[1..(afterLocale + 1)]);

        if (locale is null)
        {
            // Not a localized page: root files such as /robots.txt are served by the static file middleware.
            return next(context);
        }

        if (afterLocale < 0)
        {
            // "/de" -> "/de/": the app's <base href> needs the trailing slash.
            context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
            context.Response.Headers.Location = $"/{locale}/{request.QueryString}";
            return Task.CompletedTask;
        }

        if (!IsFile(path.Value) && !LooksLikeAsset(value))
        {
            var page = $"{path.Value.TrimEnd('/')}/{PrerenderedPageFileName}";
            request.Path = IsFile(page) ? page : $"/{locale}/{ClientShellFileName}";
        }

        return next(context);
    }

    private static void RedirectToNegotiatedLanguage(HttpContext context)
    {
        var language = LanguageNegotiator.Negotiate(context.Request);
        var headers = context.Response.Headers;

        context.Response.StatusCode = StatusCodes.Status302Found;
        headers.Location = $"/{language}/{context.Request.QueryString}";
        // The target depends on the visitor, so neither browsers nor proxies may reuse it for someone else.
        headers.CacheControl = "no-store";
        headers.Vary = $"{HeaderNames.AcceptLanguage}, {HeaderNames.Cookie}";
    }

    private bool IsFile(string path)
    {
        var file = _files.GetFileInfo(path);

        return file.Exists && !file.IsDirectory;
    }

    /// <summary>A missing <c>main-ABC.js</c> must be a 404, not the HTML shell.</summary>
    private static bool LooksLikeAsset(ReadOnlySpan<char> path)
    {
        var lastSegment = path[(path.LastIndexOf('/') + 1)..];

        return lastSegment.Contains('.');
    }
}
