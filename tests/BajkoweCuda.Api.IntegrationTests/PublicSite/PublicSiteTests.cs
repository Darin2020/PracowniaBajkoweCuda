using System.Net;

namespace BajkoweCuda.Api.IntegrationTests.PublicSite;

public sealed class PublicSiteTests(SiteFactory factory) : IClassFixture<SiteFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateSiteClient();

    private Task<HttpResponseMessage> GetAsync(string path, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));

        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return _client.SendAsync(request, Ct);
    }

    private static Task<string> BodyAsync(HttpResponseMessage response) => response.Content.ReadAsStringAsync(Ct);

    // ---- "/" language redirect -------------------------------------------------------------

    [Fact]
    public async Task Root_redirects_to_English_when_the_visitor_states_no_preference()
    {
        using var response = await GetAsync("/");

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location?.OriginalString.ShouldBe("/en/");
    }

    [Theory]
    [InlineData("de", "/de/")]
    [InlineData("fr-CH, fr;q=0.9, en;q=0.8", "/fr/")]
    [InlineData("it, pl;q=0.7, en;q=0.3", "/pl/")]
    [InlineData("en;q=0.2, es;q=0.9", "/es/")]
    [InlineData("PL-pl", "/pl/")]
    [InlineData("it, ja", "/en/")]
    [InlineData("de;q=0, fr;q=0.5", "/fr/")]
    [InlineData("not a valid header ;;;", "/en/")]
    public async Task Root_redirects_by_Accept_Language(string acceptLanguage, string expected)
    {
        using var response = await GetAsync("/", ("Accept-Language", acceptLanguage));

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location?.OriginalString.ShouldBe(expected);
    }

    [Fact]
    public async Task Root_prefers_the_lang_cookie_over_Accept_Language()
    {
        using var response = await GetAsync("/", ("Cookie", "lang=pl"), ("Accept-Language", "de"));

        response.Headers.Location?.OriginalString.ShouldBe("/pl/");
    }

    [Theory]
    [InlineData("lang=xx")]
    [InlineData("lang=")]
    [InlineData("lang=../../etc")]
    [InlineData("lang=https://evil.example")]
    public async Task Root_ignores_a_lang_cookie_that_is_not_a_supported_locale(string cookie)
    {
        using var response = await GetAsync("/", ("Cookie", cookie), ("Accept-Language", "de"));

        response.Headers.Location?.OriginalString.ShouldBe("/de/");
    }

    [Fact]
    public async Task Root_redirect_is_not_cacheable_and_keeps_the_query_string()
    {
        using var response = await GetAsync("/?utm_source=test");

        response.Headers.Location?.OriginalString.ShouldBe("/en/?utm_source=test");
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
        response.Headers.Vary.ShouldContain("Accept-Language");
        response.Headers.Vary.ShouldContain("Cookie");
    }

    // ---- prerendered pages -----------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Locales))]
    public async Task Locale_root_serves_the_prerendered_landing_page(string locale)
    {
        using var response = await GetAsync($"/{locale}/");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        (await BodyAsync(response)).ShouldBe(SiteFactory.Landing(locale));
    }

    [Theory]
    [InlineData("/de/privacy")]
    [InlineData("/de/privacy/")]
    public async Task Prerendered_page_is_served_with_or_without_a_trailing_slash(string path)
    {
        using var response = await GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(response)).ShouldBe(SiteFactory.Privacy("de"));
    }

    [Fact]
    public async Task Locale_without_trailing_slash_redirects_permanently()
    {
        using var response = await GetAsync("/pl?x=1");

        response.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently);
        response.Headers.Location?.OriginalString.ShouldBe("/pl/?x=1");
    }

    [Fact]
    public async Task Html_must_be_revalidated_on_every_visit()
    {
        using var response = await GetAsync("/en/");

        response.Headers.CacheControl?.ToString().ShouldBe("no-cache");
    }

    // ---- client-side fallback --------------------------------------------------------------

    [Theory]
    [InlineData("/fr/chat")]
    [InlineData("/fr/orders/018f6f2e-0000-7000-8000-000000000000")]
    [InlineData("/fr/admin/conversations/")]
    [InlineData("/fr/no-such-page")]
    public async Task Unknown_page_under_a_locale_falls_back_to_that_locales_client_shell(string path)
    {
        using var response = await GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.ToString().ShouldBe("no-cache");
        (await BodyAsync(response)).ShouldBe(SiteFactory.Shell("fr"));
    }

    [Theory]
    [InlineData("/en/main-MISSING0.js")]
    [InlineData("/en/assets/nope.png")]
    [InlineData("/en/privacy/extra.css")]
    public async Task Missing_asset_is_a_404_not_the_html_shell(string path)
    {
        using var response = await GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldNotBe("text/html");
    }

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/api")]
    [InlineData("/hubs/unknown")]
    [InlineData("/API/Whatever")]
    public async Task Unknown_backend_path_is_a_problem_details_404(string path)
    {
        using var response = await GetAsync(path, ("Accept", "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/hubs/unknown")]
    public async Task Backend_paths_never_fall_back_to_html_even_for_a_browser(string path)
    {
        using var response = await GetAsync(path, ("Accept", "text/html,application/xhtml+xml"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldNotBe("text/html");
        (await BodyAsync(response)).ShouldNotContain("<html");
    }

    [Theory]
    [InlineData("/xx/")]
    [InlineData("/EN/")]
    [InlineData("/english/")]
    [InlineData("/privacy")]
    [InlineData("/index.html")]
    public async Task Paths_outside_a_supported_locale_are_not_found(string path)
    {
        using var response = await GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_GET_and_HEAD_reach_the_site()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/en/", UriKind.Relative));
        using var response = await _client.SendAsync(request, Ct);

        response.IsSuccessStatusCode.ShouldBeFalse();
        (await BodyAsync(response)).ShouldNotContain("landing");
    }

    // ---- assets ----------------------------------------------------------------------------

    [Theory]
    [InlineData("/en/main-KRBIKYHT.js", "text/javascript")]
    [InlineData("/en/chunk-BxRInVTD.js", "text/javascript")]
    [InlineData("/en/styles-VN3VGPQH.css", "text/css")]
    public async Task Hashed_bundles_are_cached_forever(string path, string contentType)
    {
        using var response = await GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe(contentType);
        response.Headers.CacheControl?.ToString().ShouldBe("public, max-age=31536000, immutable");
    }

    [Theory]
    [InlineData("/en/icon.svg")]
    [InlineData("/robots.txt")]
    [InlineData("/sitemap.xml")]
    public async Task Unhashed_files_are_cached_briefly(string path)
    {
        using var response = await GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.ToString().ShouldBe("public, max-age=3600");
    }

    [Fact]
    public async Task Text_assets_are_compressed_when_the_browser_accepts_it()
    {
        using var response = await GetAsync("/en/main-KRBIKYHT.js", ("Accept-Encoding", "br"));

        response.Content.Headers.ContentEncoding.ShouldContain("br");
    }

    // ---- security headers ------------------------------------------------------------------

    [Theory]
    [InlineData("/en/")]
    [InlineData("/en/chat")]
    [InlineData("/en/main-KRBIKYHT.js")]
    [InlineData("/")]
    [InlineData("/api/does-not-exist")]
    [InlineData("/health/live")]
    public async Task Every_response_carries_the_security_headers(string path)
    {
        using var response = await GetAsync(path);

        var csp = response.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem();
        csp.ShouldContain("default-src 'self'");
        csp.ShouldContain("script-src 'self';");
        csp.ShouldContain("object-src 'none'");
        csp.ShouldContain("base-uri 'self'");
        csp.ShouldContain("form-action 'self'");
        csp.ShouldContain("frame-ancestors 'none'");

        response.Headers.GetValues("X-Content-Type-Options").ShouldHaveSingleItem().ShouldBe("nosniff");
        response.Headers.GetValues("Referrer-Policy").ShouldHaveSingleItem().ShouldBe("strict-origin-when-cross-origin");
        response.Headers.GetValues("Cross-Origin-Opener-Policy").ShouldHaveSingleItem().ShouldBe("same-origin");
        response.Headers.GetValues("X-Frame-Options").ShouldHaveSingleItem().ShouldBe("DENY");
        response.Headers.GetValues("Permissions-Policy").ShouldHaveSingleItem().ShouldContain("camera=()");
        response.Headers.Contains("Server").ShouldBeFalse();
    }

    [Fact]
    public async Task Scripts_may_not_be_inline_or_evaluated()
    {
        using var response = await GetAsync("/en/");

        var scriptSrc = response.Headers.GetValues("Content-Security-Policy").Single()
            .Split(';', StringSplitOptions.TrimEntries)
            .Single(directive => directive.StartsWith("script-src", StringComparison.Ordinal));

        scriptSrc.ShouldBe("script-src 'self'");
    }

    public static TheoryData<string> Locales() => [.. SiteFactory.Locales];
}
