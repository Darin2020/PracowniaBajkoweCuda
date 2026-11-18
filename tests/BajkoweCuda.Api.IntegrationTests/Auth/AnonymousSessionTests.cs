using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BajkoweCuda.Api.IntegrationTests.PublicSite;

namespace BajkoweCuda.Api.IntegrationTests.Auth;

/// <summary>No database here (SiteFactory's database is unreachable): these paths must not need one.</summary>
public sealed class AnonymousSessionTests(SiteFactory factory) : IClassFixture<SiteFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Me_answers_anonymous_visitors_without_the_database_and_issues_the_xsrf_cookie()
    {
        var browser = new BrowserHandler();
        using var client = factory.CreateDefaultClient(BrowserHandler.BaseAddress, browser);

        using var response = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        me.GetProperty("authenticated").GetBoolean().ShouldBeFalse();
        me.EnumerateObject().Count().ShouldBe(1);
        browser.Cookie("XSRF-TOKEN").ShouldNotBeNullOrEmpty();
        response.Headers.GetValues("Set-Cookie").ShouldContain(cookie => cookie.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal) && !cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_plain_http_origin_gets_unprefixed_cookies_that_work_without_https()
    {
        // Local development (docker compose, ng serve) runs on http://localhost, where __Host- cookies cannot be set.
        using var client = factory.CreateSiteClient(("PublicBaseUrl", "http://localhost:8080"));

        using var response = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        cookies.ShouldContain(cookie => cookie.StartsWith("bc.af=", StringComparison.Ordinal));
        cookies.ShouldNotContain(cookie => cookie.StartsWith("__Host-", StringComparison.Ordinal));
        cookies.ShouldNotContain(cookie => cookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Protected_endpoints_answer_401_problem_details_not_a_redirect()
    {
        using var client = factory.CreateDefaultClient(BrowserHandler.BaseAddress, new BrowserHandler());

        using var response = await client.GetAsync(new Uri("/api/account", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await AuthApp.ProblemCodeAsync(response)).ShouldBe("auth.not_authenticated");
    }
}
