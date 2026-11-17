namespace BajkoweCuda.Api.Observability;

internal static class VersionEndpoint
{
    public const string Path = "/api/version";

    /// <summary>
    /// Tells which build is running. Public on purpose: the deployment pipeline reads it to confirm that the new
    /// revision is the one serving traffic, and the repository (with its commit history) is public anyway.
    /// </summary>
    public static IEndpointRouteBuilder MapVersionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(Path, (AppVersion version, IHostEnvironment environment, HttpResponse response) =>
            {
                response.Headers.CacheControl = "no-store";

                return TypedResults.Ok(new VersionResponse(version.Value, environment.EnvironmentName));
            })
            .AllowAnonymous();

        return endpoints;
    }

    internal sealed record VersionResponse(string Version, string Environment);
}
