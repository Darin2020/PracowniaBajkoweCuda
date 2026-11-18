using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using BajkoweCuda.Api.Auth;
using BajkoweCuda.Api.Problems;
using Microsoft.Extensions.Options;

namespace BajkoweCuda.Api.Security;

/// <summary>Limits of docs/PLAN.md §8.3. Configurable so tests can raise them.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Sign-in, registration, 2FA and re-authentication: requests per minute per IP address.</summary>
    public int AuthPerMinute { get; set; } = 10;

    /// <summary>Requests that send an email (resend confirmation, forgot password): per hour per IP address and per email address.</summary>
    public int EmailsPerHour { get; set; } = 3;

    /// <summary>Admin endpoints: requests per minute per admin. Generous; it only stops runaway clients.</summary>
    public int AdminPerMinute { get; set; } = 300;

    /// <summary>Chat image uploads: per hour per user.</summary>
    public int UploadsPerHour { get; set; } = 30;
}

/// <summary>
/// ASP.NET Core rate limiter policies (§8.3). Counters are kept in memory per replica, which is enough while each
/// environment runs one replica; several replicas would each allow the full limit.
/// </summary>
internal static class RateLimiting
{
    public const string Auth = "auth";
    public const string Email = "email";
    public const string Admin = "admin";
    public const string Uploads = "uploads";

    public static IServiceCollection AddRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));
        services.AddSingleton<EmailThrottle>();

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(Auth, context => RateLimitPartition.GetSlidingWindowLimiter(
                ClientAddress(context),
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = Limits(context).AuthPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0,
                }));

            options.AddPolicy(Email, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientAddress(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Limits(context).EmailsPerHour,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                }));

            options.AddPolicy(Admin, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ClientAddress(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Limits(context).AdminPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            options.AddPolicy(Uploads, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ClientAddress(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Limits(context).UploadsPerHour,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                // Fixed windows know when they reopen; the sliding window does not say, so a minute (its length) it is.
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var known) ? known : TimeSpan.FromMinutes(1);
                context.HttpContext.Response.Headers.RetryAfter =
                    Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                await ApiProblems.WriteAsync(context.HttpContext, AuthErrors.RateLimited);
            };
        });

        return services;
    }

    private static RateLimitOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

    /// <summary>Behind the ingress this is the address from X-Forwarded-For (ASPNETCORE_FORWARDEDHEADERS_ENABLED).</summary>
    private static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

/// <summary>
/// At most <see cref="RateLimitOptions.EmailsPerHour"/> emails per hour to one address, whoever asks (§8.3), so the
/// public forms cannot be used to flood someone's mailbox. Over the limit, the email is silently not sent: the
/// response must not reveal anything about the address.
/// </summary>
public sealed class EmailThrottle : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public EmailThrottle(IOptions<RateLimitOptions> options)
    {
        var permits = options.Value.EmailsPerHour;
        _limiter = PartitionedRateLimiter.Create<string, string>(address => RateLimitPartition.GetFixedWindowLimiter(
            address,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    }

    public bool TryAcquire(string email)
    {
        using var lease = _limiter.AttemptAcquire(email.Trim().ToUpperInvariant());
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
