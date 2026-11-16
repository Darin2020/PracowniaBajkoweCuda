namespace BajkoweCuda.Api.Security;

/// <summary>Adds the HTTP hardening headers to every response (docs/PLAN.md §8.2).</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Everything comes from this origin. Scripts are external files only: no inline scripts, no <c>eval</c>.
    /// <c>style-src</c> allows inline styles because Angular puts component styles into <c>&lt;style&gt;</c>
    /// elements, both in the prerendered HTML and at runtime (see docs/DECISIONS.md).
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "font-src 'self'; " +
        "connect-src 'self' wss:; " +
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
            var headers = ((HttpContext)state).Response.Headers;

            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = PermissionsPolicy;
            headers["Cross-Origin-Opener-Policy"] = "same-origin";

            return Task.CompletedTask;
        }, context);

        return next(context);
    }
}
