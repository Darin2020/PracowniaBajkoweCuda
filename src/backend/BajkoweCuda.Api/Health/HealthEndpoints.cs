using BajkoweCuda.Infrastructure.Chat;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace BajkoweCuda.Api.Health;

internal static class HealthEndpoints
{
    public const string LivePath = "/health/live";
    public const string ReadyPath = "/health/ready";

    private const string ReadyTag = "ready";

    public static IServiceCollection AddHealth(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: [ReadyTag])
            .AddStorageCheck(ReadyTag);

        return services;
    }

    /// <summary>
    /// Liveness runs no dependency checks, so container probes never touch (and never wake up) the database.
    /// Readiness checks the dependencies; only the deployment smoke test calls it (docs/PLAN.md §6, Health).
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapHealthChecks(LivePath, new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        endpoints
            .MapHealthChecks(ReadyPath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) })
            .AllowAnonymous();

        return endpoints;
    }
}
