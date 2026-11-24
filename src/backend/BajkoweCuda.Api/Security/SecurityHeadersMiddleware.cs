namespace BajkoweCuda.Api.Security;

/// <summary>Adds the HTTP hardening headers to every response (docs/PLAN.md §8.2).</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Everything comes from this origin. Scripts are external files only: no inline scripts, no <c>eval</c>.
    /// Inline <c>&lt;style&gt;</c> elements (Angular's component styles, in the prerendered HTML and at runtime) need
    /// this response's nonce; style attributes in markup are not allowed. <c>'self'</c> in <c>connect-src</c> also
    /// covers the same-origin WebSocket of SignalR (see docs/DECISIONS.md).
    /// </summary>
    public static string ContentSecurityPolicy(string nonce) =>
        "default-src 'self'; " +
        "script-src 'self'; " +
        $"style-src 'self' 'nonce-{nonce}'; " +
        "img-src 'self' data: blob:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public const string PermissionsPolicy =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var http = (HttpContext)state;
            var headers = http.Response.Headers;

            headers.ContentSecurityPolicy = ContentSecurityPolicy(CspNonce.For(http));
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = PermissionsPolicy;
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            // Nothing here is embedded by other sites, and the site embeds nothing from others.
            headers["Cross-Origin-Embedder-Policy"] = "require-corp";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";

            return Task.CompletedTask;
        }, context);

        return next(context);
    }
}
