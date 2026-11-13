using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BajkoweCuda.Api.IntegrationTests;

public class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
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
