using System.Text;
using BajkoweCuda.Api.Security;
using Microsoft.Extensions.FileProviders;

namespace BajkoweCuda.Api.Hosting;

/// <summary>
/// Sends the site's HTML pages with this response's CSP nonce in place of <see cref="CspNonce.Placeholder"/>.
/// Because every response differs, pages have no <c>ETag</c> or <c>Last-Modified</c> and are never answered with
/// 304; they stay small and <c>no-cache</c>. Other files are left to the static file middleware.
/// </summary>
internal sealed class HtmlPageMiddleware(RequestDelegate next, PublicSiteFiles files)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path.Value;

        if ((!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            || path is null
            || !path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var file = files.GetFileInfo(path);
        if (!file.Exists || file.IsDirectory)
        {
            await next(context);
            return;
        }

        var body = Utf8.GetBytes((await ReadAsync(file, context.RequestAborted))
            .Replace(CspNonce.Placeholder, CspNonce.For(context), StringComparison.Ordinal));

        var response = context.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength = body.Length;
        response.Headers.CacheControl = StaticAssetCaching.Html;

        if (HttpMethods.IsGet(request.Method))
        {
            await response.Body.WriteAsync(body, context.RequestAborted);
        }
    }

    private static async Task<string> ReadAsync(IFileInfo file, CancellationToken cancellationToken)
    {
        await using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
