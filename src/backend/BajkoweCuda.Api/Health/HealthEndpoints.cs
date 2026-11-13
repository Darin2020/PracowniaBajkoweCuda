using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace BajkoweCuda.Api.Health;

internal static class HealthEndpoints
{
    public const string LivePath = "/health/live";

    /// <summary>
    /// Liveness only: it runs no dependency checks, so container probes never touch (and never wake up) the database.
    /// The readiness endpoint (database + blob storage) is added together with those dependencies.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapHealthChecks(LivePath, new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        return endpoints;
    }
}
