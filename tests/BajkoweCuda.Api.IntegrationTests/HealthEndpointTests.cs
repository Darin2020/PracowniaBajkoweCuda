using System.Net;
using BajkoweCuda.Api.IntegrationTests.Database;
using BajkoweCuda.Api.IntegrationTests.PublicSite;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BajkoweCuda.Api.IntegrationTests;

public class HealthEndpointTests(SiteFactory factory, SqlServerFixture sqlServer) : IClassFixture<SiteFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Live_returns_200_for_anonymous_requests()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Live_does_not_touch_the_database()
    {
        // The factory's database is unreachable (SiteFactory.UnreachableDatabase): liveness must still be healthy.
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_returns_503_when_the_database_is_unreachable()
    {
        using var client = factory.CreateSiteClient(("ConnectionStrings:Sql", SiteFactory.UnreachableDatabase));

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    [Trait("Category", SqlServerFixture.Category)]
    public async Task Ready_returns_200_when_the_database_is_reachable()
    {
        var connectionString = await sqlServer.CreateDatabaseAsync();
        var app = factory.WithSettings(("ConnectionStrings:Sql", connectionString));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().RunAsync(Ct);
        }

        using var client = app.CreateClient();
        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Responses_do_not_expose_the_server_header()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct);

        response.Headers.Contains("Server").ShouldBeFalse();
    }

    [Fact]
    public async Task Unknown_api_path_returns_problem_details_not_html()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }
}
